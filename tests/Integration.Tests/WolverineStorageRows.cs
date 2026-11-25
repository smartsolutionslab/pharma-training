using System.Text;
using Aspire.Hosting.Testing;
using Npgsql;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// Rows of Wolverine's storage in a service's own database, planted and counted by hand (#115, #439): a dead letter and
/// a handled envelope are what Wolverine leaves, a scheduled one is not due for a day, and an outgoing one belongs to no
/// node of this run, so none is delivered while a test looks. A tombstone in the module's <c>erased_persons</c> is what
/// an erasure leaves; a test that plants one uses a fresh person, so nothing another test waits for is ever swept.
/// </summary>
internal static class WolverineStorageRows
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan DeadLetterTimeout = TimeSpan.FromSeconds(30);

    /// <summary>A connection to the service's database, <c>&lt;service&gt;-database</c>.</summary>
    public static async Task<NpgsqlConnection> OpenAsync(AspireFixture aspire, string service, CancellationToken cancellationToken)
    {
        var connectionString = await aspire.App.GetConnectionStringAsync($"{service}-database", cancellationToken)
            ?? throw new InvalidOperationException($"{service}'s database has no connection string.");
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    /// <summary>Records the person as erased in the module's schema, as its erasure consumer does.</summary>
    public static async Task TombstoneAsync(NpgsqlConnection connection, string schema, Guid person, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand($"INSERT INTO {schema}.erased_persons (person_id, erased_at) VALUES (@id, now())", connection);
        command.Parameters.AddWithValue("id", person);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public static async Task<Guid> DeadLetterAsync(NpgsqlConnection connection, string service, string type, Guid person, CancellationToken cancellationToken)
    {
        var id = Guid.CreateVersion7();
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO wolverine.wolverine_dead_letters (id, execution_time, body, message_type, received_at, source, exception_type, exception_message, sent_at, replayable)
            VALUES (@id, NULL, @body, @type, 'rabbitmq://queue/' || @service, 'rabbitmq://exchange/' || @type, 'System.Exception', 'planted by WolverineStorageRows', now(), false)
            """,
            connection);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("body", Body(person));
        command.Parameters.AddWithValue("type", type);
        command.Parameters.AddWithValue("service", service);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return id;
    }

    /// <summary>An envelope of the durable inbox in <paramref name="status"/>, kept and due a day from now, so neither Wolverine's retention nor its scheduler touches it.</summary>
    public static async Task<Guid> IncomingAsync(NpgsqlConnection connection, string service, string type, Guid person, string status, CancellationToken cancellationToken)
    {
        var id = Guid.CreateVersion7();
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO wolverine.wolverine_incoming_envelopes (id, status, owner_id, execution_time, attempts, body, message_type, received_at, keep_until)
            VALUES (@id, @status, 0, now() + interval '1 day', 0, @body, @type, 'rabbitmq://queue/' || @service, now() + interval '1 day')
            """,
            connection);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("status", status);
        command.Parameters.AddWithValue("body", Body(person));
        command.Parameters.AddWithValue("type", type);
        command.Parameters.AddWithValue("service", service);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return id;
    }

    public static async Task<Guid> OutgoingAsync(NpgsqlConnection connection, string type, Guid person, CancellationToken cancellationToken)
    {
        var id = Guid.CreateVersion7();
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO wolverine.wolverine_outgoing_envelopes (id, owner_id, destination, deliver_by, body, attempts, message_type)
            VALUES (@id, 2147483000, 'rabbitmq://exchange/purge-probe', now() + interval '1 day', @body, 0, @type)
            """,
            connection);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("body", Body(person));
        command.Parameters.AddWithValue("type", type);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return id;
    }

    public static async Task<long> CountAsync(NpgsqlConnection connection, string table, Guid[] ids, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand($"SELECT count(*) FROM wolverine.{table} WHERE id = ANY(@ids)", connection);
        command.Parameters.AddWithValue("ids", ids);
        return (long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    public static async Task DeleteAsync(NpgsqlConnection connection, string table, Guid id, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand($"DELETE FROM wolverine.{table} WHERE id = @id", connection);
        command.Parameters.AddWithValue("id", id);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>Waits until none of the rows is left in the table; fails naming the table and how many are left.</summary>
    public static async Task WaitUntilGoneAsync(NpgsqlConnection connection, string table, Guid[] ids, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var started = TimeProvider.System.GetTimestamp();
        while (true)
        {
            var left = await CountAsync(connection, table, ids, cancellationToken);
            if (left == 0)
            {
                return;
            }

            var waited = TimeProvider.System.GetElapsedTime(started);
            if (waited >= timeout)
            {
                throw new TimeoutException($"wolverine.{table} still holds {left} of {ids.Length} planted rows after {waited.TotalSeconds:0} s.");
            }

            await Task.Delay(PollInterval, cancellationToken);
        }
    }

    /// <summary>Waits until the service's Wolverine has dead-lettered the message and answers the exception it was dead-lettered with.</summary>
    public static async Task<string> WaitForDeadLetterAsync(AspireFixture aspire, string service, Guid messageId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(aspire, service, cancellationToken);
        var started = TimeProvider.System.GetTimestamp();
        while (true)
        {
            await using var command = new NpgsqlCommand("SELECT exception_type FROM wolverine.wolverine_dead_letters WHERE id = @id", connection);
            command.Parameters.AddWithValue("id", messageId);
            if (await command.ExecuteScalarAsync(cancellationToken) is string exception)
            {
                return exception;
            }

            if (TimeProvider.System.GetElapsedTime(started) >= DeadLetterTimeout)
            {
                throw new TimeoutException($"{service} had not dead-lettered message {messageId} after {DeadLetterTimeout.TotalSeconds} s.");
            }

            await Task.Delay(PollInterval, cancellationToken);
        }
    }

    /// <summary>A membership message's body as camelCase JSON, holding the person's id in its D form.</summary>
    public static byte[] Body(Guid person) => Body(person, $"Purge Probe {person:N}");

    public static byte[] Body(Guid person, string name) =>
        Encoding.UTF8.GetBytes($$"""{"membershipId":"{{Guid.CreateVersion7()}}","personId":"{{person}}","pharmacyId":"{{Guid.CreateVersion7()}}","displayName":"{{name}}"}""");
}
