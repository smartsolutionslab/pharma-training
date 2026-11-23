using Aspire.Hosting.Testing;
using Npgsql;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// Compliance's side of the tests: an event sent as its publishing service's Wolverine sends it, the barriers that
/// Compliance's inbox handled it or dead-lettered it, and Compliance's own tables read directly.
/// </summary>
internal static class ComplianceDatabase
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(30);

    // Seven attempts, the waits doubling from 1 to 32 seconds, take about a minute before the dead letter.
    private static readonly TimeSpan DeadLetterTimeout = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    /// <summary>Sends the event under a new envelope id and waits until Compliance's inbox has handled it.</summary>
    public static async Task DeliverAsync<T>(AspireFixture aspire, string type, T message, CancellationToken cancellationToken)
    {
        var messageId = Guid.CreateVersion7();
        await SendAsync(aspire, type, message, messageId, cancellationToken);
        await WaitUntilHandledAsync(aspire, messageId, cancellationToken);
    }

    public static Task SendAsync<T>(AspireFixture aspire, string type, T message, Guid messageId, CancellationToken cancellationToken) =>
        Broker.SendAsync(aspire, type, message, messageId, cancellationToken);

    /// <summary>
    /// The barrier: Compliance's durable inbox marks the envelope handled once its consumer is done with it. A later
    /// message is no barrier, since a listener handles its messages in parallel.
    /// </summary>
    public static async Task WaitUntilHandledAsync(AspireFixture aspire, Guid messageId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(aspire, cancellationToken);
        var started = TimeProvider.System.GetTimestamp();
        while (true)
        {
            await using var command = new NpgsqlCommand("select status from wolverine.wolverine_incoming_envelopes where id = @id", connection);
            command.Parameters.AddWithValue("id", messageId);
            var status = await command.ExecuteScalarAsync(cancellationToken) as string;
            if (status == "Handled")
            {
                return;
            }

            if (TimeProvider.System.GetElapsedTime(started) >= WaitTimeout)
            {
                throw new TimeoutException($"Compliance's inbox held envelope {messageId} as {status ?? "nothing"}, not Handled, after {WaitTimeout.TotalSeconds} s.");
            }

            await Task.Delay(PollInterval, cancellationToken);
        }
    }

    /// <summary>
    /// Waits until Compliance has given up on the envelope, after its retries (about a minute), and answers the reason
    /// its dead letter holds.
    /// </summary>
    public static async Task<string> DeadLetteredAsync(AspireFixture aspire, Guid messageId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(aspire, cancellationToken);
        var started = TimeProvider.System.GetTimestamp();
        while (true)
        {
            await using var command = new NpgsqlCommand("select exception_message from wolverine.wolverine_dead_letters where id = @id", connection);
            command.Parameters.AddWithValue("id", messageId);
            if (await command.ExecuteScalarAsync(cancellationToken) is string reason)
            {
                return reason;
            }

            if (TimeProvider.System.GetElapsedTime(started) >= DeadLetterTimeout)
            {
                throw new TimeoutException($"Compliance had not dead-lettered envelope {messageId} after {DeadLetterTimeout.TotalSeconds} s.");
            }

            await Task.Delay(PollInterval, cancellationToken);
        }
    }

    /// <summary>The rows a counting query finds for the id.</summary>
    public static async Task<long> CountAsync(AspireFixture aspire, string query, Guid id, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(aspire, cancellationToken);
        await using var command = new NpgsqlCommand(query, connection);
        command.Parameters.AddWithValue("id", id);
        return (long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    public static async Task<NpgsqlConnection> OpenAsync(AspireFixture aspire, CancellationToken cancellationToken)
    {
        var connectionString = await aspire.App.GetConnectionStringAsync("compliance-database", cancellationToken)
            ?? throw new InvalidOperationException("Compliance's database has no connection string.");
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }
}
