using Aspire.Hosting.Testing;
using Npgsql;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// Compliance's side of the tests: an event sent as its publishing service's Wolverine sends it, the barriers that
/// Compliance's inbox handled it or dead-lettered it, and Compliance's own tables read directly.
/// </summary>
internal static class ComplianceDatabase
{
    /// <summary>The exception a message the publisher check refuses is dead-lettered with.</summary>
    private const string Forged = "SmartSolutionsLab.PharmaTraining.Host.Messaging.ForgedPublisherException";

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

    /// <summary>
    /// The barrier before a scheduled retry: Compliance's consumer threw, and Wolverine rescheduled the envelope in the
    /// durable inbox (<c>Scheduled</c>), from where it loads it again for the next attempt.
    /// </summary>
    public static async Task WaitUntilScheduledForRetryAsync(AspireFixture aspire, Guid messageId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(aspire, cancellationToken);
        var started = TimeProvider.System.GetTimestamp();
        while (true)
        {
            var status = await StatusAsync(connection, messageId, cancellationToken);
            if (status == "Scheduled")
            {
                return;
            }

            if (TimeProvider.System.GetElapsedTime(started) >= WaitTimeout)
            {
                throw new TimeoutException($"Compliance's inbox held envelope {messageId} as {status ?? "nothing"}, not Scheduled for a retry, after {WaitTimeout.TotalSeconds} s.");
            }

            await Task.Delay(PollInterval, cancellationToken);
        }
    }

    /// <summary>
    /// Waits until Compliance's inbox has handled the envelope, and fails the test at once when it was dead-lettered as
    /// forged: an envelope Wolverine loaded again from its storage (a retry, a recovery, a replay) that lost its publisher.
    /// </summary>
    public static async Task WaitUntilHandledUnlessForgedAsync(AspireFixture aspire, Guid messageId, TimeSpan timeout, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(aspire, cancellationToken);
        var started = TimeProvider.System.GetTimestamp();
        while (true)
        {
            var status = await StatusAsync(connection, messageId, cancellationToken);
            if (status == "Handled")
            {
                return;
            }

            var deadLetter = await DeadLetterAsync(connection, messageId, cancellationToken);
            if (deadLetter is { Type: Forged })
            {
                throw new ShouldAssertException($"Compliance dead-lettered envelope {messageId} as forged when it was loaded again: {deadLetter.Message}");
            }

            if (TimeProvider.System.GetElapsedTime(started) >= timeout)
            {
                throw new TimeoutException($"Compliance's inbox held envelope {messageId} as {status ?? "nothing"}, not Handled, after {timeout.TotalSeconds} s; its dead letter: {deadLetter?.Type ?? "none"}.");
            }

            await Task.Delay(PollInterval, cancellationToken);
        }
    }

    /// <summary>Removes a stored course version, so an attempt that conflicted with it succeeds the next time.</summary>
    public static async Task DeleteCourseVersionAsync(AspireFixture aspire, Guid courseVersionId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(aspire, cancellationToken);
        await using var command = new NpgsqlCommand("DELETE FROM compliance.course_versions WHERE id = @id", connection);
        command.Parameters.AddWithValue("id", courseVersionId);
        (await command.ExecuteNonQueryAsync(cancellationToken)).ShouldBe(1, $"Compliance stores no course version {courseVersionId}.");
    }

    /// <summary>Marks the dead letter replayable, so Wolverine delivers it again.</summary>
    public static async Task ReplayAsync(AspireFixture aspire, Guid deadLetter, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(aspire, cancellationToken);
        await using var command = new NpgsqlCommand("UPDATE wolverine.wolverine_dead_letters SET replayable = true WHERE id = @id", connection);
        command.Parameters.AddWithValue("id", deadLetter);
        (await command.ExecuteNonQueryAsync(cancellationToken)).ShouldBe(1, $"Compliance holds no dead letter {deadLetter}.");
    }

    /// <summary>
    /// Hands a stored envelope of Compliance's durable inbox back to Wolverine as one no node has taken yet
    /// (<c>Incoming</c>, owner 0, no attempts), as a node that died while handling it leaves it; the durability agent
    /// recovers it and delivers it again.
    /// </summary>
    public static async Task ReturnToInboxAsync(AspireFixture aspire, Guid envelope, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(aspire, cancellationToken);
        await using var command = new NpgsqlCommand(
            "UPDATE wolverine.wolverine_incoming_envelopes SET status = 'Incoming', owner_id = 0, attempts = 0, keep_until = NULL WHERE id = @id",
            connection);
        command.Parameters.AddWithValue("id", envelope);
        (await command.ExecuteNonQueryAsync(cancellationToken)).ShouldBe(1, $"There is no stored envelope {envelope}.");
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

    /// <summary>The envelope's status in Compliance's durable inbox, or nothing when the inbox does not hold it.</summary>
    private static async Task<string?> StatusAsync(NpgsqlConnection connection, Guid messageId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("select status from wolverine.wolverine_incoming_envelopes where id = @id", connection);
        command.Parameters.AddWithValue("id", messageId);
        return await command.ExecuteScalarAsync(cancellationToken) as string;
    }

    /// <summary>The exception's type and message of the envelope's dead letter, or nothing when there is none.</summary>
    private static async Task<DeadLetter?> DeadLetterAsync(NpgsqlConnection connection, Guid messageId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("select exception_type, exception_message from wolverine.wolverine_dead_letters where id = @id", connection);
        command.Parameters.AddWithValue("id", messageId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new DeadLetter(
            await reader.IsDBNullAsync(0, cancellationToken) ? null : reader.GetString(0),
            await reader.IsDBNullAsync(1, cancellationToken) ? null : reader.GetString(1));
    }

    private sealed record DeadLetter(string? Type, string? Message);
}
