using Aspire.Hosting.Testing;
using Npgsql;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// What a test does in the editorial database itself: hold a version's rows so concurrent requests wait on them, see
/// that they wait and that PostgreSQL ended a race as a deadlock, and publish a version before the API can (#18).
/// </summary>
internal static class EditorialDatabase
{
    // Every backend that waits for the gate's backend, directly or behind another that waits for it: a second request
    // that waits for a row behind the first is blocked by the first, not by the gate.
    private const string BlockedBehindGate =
        """
        WITH RECURSIVE behind(pid) AS (
            SELECT activity.pid FROM pg_stat_activity activity WHERE @gate = ANY(pg_blocking_pids(activity.pid))
            UNION
            SELECT activity.pid FROM pg_stat_activity activity JOIN behind ON behind.pid = ANY(pg_blocking_pids(activity.pid))
        )
        SELECT count(*) FROM behind
        """;

    public static async Task<string> ConnectionStringAsync(AspireFixture aspire, CancellationToken cancellationToken)
    {
        return await aspire.App.GetConnectionStringAsync("editorial-database", cancellationToken)
            ?? throw new InvalidOperationException("The editorial database has no connection string.");
    }

    /// <summary>
    /// Opens a transaction that holds the version's row, its chapters', its questions' and their options' rows, so every
    /// request that writes the version or anything beneath it waits after it has loaded the version until the
    /// transaction ends. Disposing the gate closes its connection.
    /// </summary>
    public static Task<VersionLock> LockVersionAsync(string connectionString, Guid id, CancellationToken cancellationToken) =>
        LockAsync(
            connectionString,
            id,
            "FOR UPDATE",
            [
                "SELECT 1 FROM editorial.course_version_chapters WHERE course_version_id = @id FOR UPDATE",
                "SELECT 1 FROM editorial.course_version_questions WHERE course_version_id = @id FOR UPDATE",
                "SELECT 1 FROM editorial.course_version_answer_options WHERE question_id IN (SELECT id FROM editorial.course_version_questions WHERE course_version_id = @id) FOR UPDATE",
            ],
            cancellationToken);

    /// <summary>
    /// Opens a transaction that holds the version's row alone, FOR NO KEY UPDATE: a request writes the rows beneath it,
    /// whose keys to the version may still check the row (FOR KEY SHARE), and then waits to update the version. Which of
    /// the requests that wait updates the version first once the gate ends is not promised: PostgreSQL does not serve
    /// its lock queue strictly in order, so a test that takes this gate accepts either order.
    /// </summary>
    public static Task<VersionLock> LockVersionRowAsync(string connectionString, Guid id, CancellationToken cancellationToken) =>
        LockAsync(connectionString, id, "FOR NO KEY UPDATE", [], cancellationToken);

    /// <summary>
    /// Opens a transaction that holds the given chapters' rows alone, so a request that writes one of them waits on it
    /// until the transaction ends, after it has written what it writes before. Disposing the gate closes its connection.
    /// </summary>
    public static async Task<VersionLock> LockChaptersAsync(string connectionString, Guid[] chapters, CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            var transaction = await connection.BeginTransactionAsync(cancellationToken);
            await using (var rows = new NpgsqlCommand("SELECT count(*) FROM (SELECT 1 FROM editorial.course_version_chapters WHERE id = ANY(@ids) FOR UPDATE) AS locked", connection, transaction))
            {
                rows.Parameters.AddWithValue("ids", chapters);
                (await rows.ExecuteScalarAsync(cancellationToken)).ShouldBe((long)chapters.Length);
            }

            return new VersionLock(connection, transaction);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    /// <summary>
    /// Opens a transaction that locks the answer options' table against writes (SHARE): a request that adds or changes
    /// options waits there, after everything it writes before them, until the transaction ends. Reads go on. It holds
    /// every version's options, so only a test in <see cref="EditorialDrafts"/> may take it. Disposing the gate closes
    /// its connection.
    /// </summary>
    public static async Task<VersionLock> LockAnswerOptionsAsync(string connectionString, CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            var transaction = await connection.BeginTransactionAsync(cancellationToken);
            await using (var locked = new NpgsqlCommand("LOCK TABLE editorial.course_version_answer_options IN SHARE MODE", connection, transaction))
            {
                await locked.ExecuteNonQueryAsync(cancellationToken);
            }

            return new VersionLock(connection, transaction);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    /// <summary>
    /// Polls the editorial database until the given number of backends wait for the gate, directly or behind each
    /// other, and fails the test when they do not within 30 seconds. It counts every backend behind the gate, whichever
    /// test sent it: a gate that locks a whole table holds every request that touches that table, so a test class whose
    /// requests touch it must be in <see cref="EditorialDrafts"/>.
    /// </summary>
    public static Task WaitUntilBlockedAsync(string connectionString, VersionLock gate, int expected, CancellationToken cancellationToken)
    {
        Ensure.That(gate).IsNotNull();
        return WaitUntilAsync(connectionString, gate, expected, cancellationToken);
    }

    /// <summary>The deadlocks PostgreSQL has detected in the editorial database since its statistics were reset.</summary>
    public static async Task<long> DeadlocksAsync(string connectionString, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("SELECT deadlocks FROM pg_stat_database WHERE datname = current_database()", connection);
        return (long)(await command.ExecuteScalarAsync(cancellationToken) ?? 0L);
    }

    /// <summary>
    /// The editorial database's deadlock count once it has settled: the same over two seconds. A backend reports a
    /// deadlock when it next goes idle, so a count read straight after another round or test may still rise for a
    /// deadlock that happened before this round; read it settled before a round whose deadlock is to be counted.
    /// </summary>
    public static async Task<long> SettledDeadlocksAsync(string connectionString, CancellationToken cancellationToken)
    {
        var started = TimeProvider.System.GetTimestamp();
        var deadlocks = await DeadlocksAsync(connectionString, cancellationToken);
        var stableSince = TimeProvider.System.GetTimestamp();
        while (TimeProvider.System.GetElapsedTime(stableSince) < TimeSpan.FromSeconds(2))
        {
            TimeProvider.System.GetElapsedTime(started).ShouldBeLessThan(TimeSpan.FromSeconds(30), "the editorial database's deadlock count never settled");
            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
            var now = await DeadlocksAsync(connectionString, cancellationToken);
            if (now != deadlocks)
            {
                deadlocks = now;
                stableSince = TimeProvider.System.GetTimestamp();
            }
        }

        return deadlocks;
    }

    /// <summary>
    /// Polls the editorial database's deadlock count until it rose above the given one, and fails the test when it does
    /// not within 30 seconds. A backend reports its statistics when it goes idle, up to about ten seconds late.
    /// </summary>
    public static async Task ShouldHaveDeadlockedSinceAsync(string connectionString, long before, CancellationToken cancellationToken)
    {
        var started = TimeProvider.System.GetTimestamp();
        var deadlocks = await DeadlocksAsync(connectionString, cancellationToken);
        while (deadlocks <= before && TimeProvider.System.GetElapsedTime(started) < TimeSpan.FromSeconds(30))
        {
            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
            deadlocks = await DeadlocksAsync(connectionString, cancellationToken);
        }

        deadlocks.ShouldBeGreaterThan(before, "no round ended in a deadlock, so the test never exercised the path it guards");
    }

    /// <summary>
    /// Publishing through the API needs submitting and publishing (#18), which need approved questions (#16) and a
    /// blueprint (#17). Until then a test publishes the version in its own database; #18 replaces this seed with the
    /// real submit and publish calls.
    /// </summary>
    public static async Task SeedPublishedAsync(string connectionString, Guid id, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("UPDATE editorial.course_versions SET status = 'Published' WHERE id = @id", connection);
        command.Parameters.AddWithValue("id", id);

        (await command.ExecuteNonQueryAsync(cancellationToken)).ShouldBe(1);
    }

    private static async Task<VersionLock> LockAsync(string connectionString, Guid id, string versionLock, string[] beneath, CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            var transaction = await connection.BeginTransactionAsync(cancellationToken);
            await using (var version = new NpgsqlCommand($"SELECT 1 FROM editorial.course_versions WHERE id = @id {versionLock}", connection, transaction))
            {
                version.Parameters.AddWithValue("id", id);
                (await version.ExecuteScalarAsync(cancellationToken)).ShouldNotBeNull();
            }

            foreach (var sql in beneath)
            {
                await using var rows = new NpgsqlCommand(sql, connection, transaction);
                rows.Parameters.AddWithValue("id", id);
                await rows.ExecuteNonQueryAsync(cancellationToken);
            }

            return new VersionLock(connection, transaction);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private static async Task WaitUntilAsync(string connectionString, VersionLock gate, int expected, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(BlockedBehindGate, connection);
        command.Parameters.AddWithValue("gate", gate.ProcessId);

        var blocked = 0L;
        while (blocked < expected)
        {
            if (timeout.IsCancellationRequested)
            {
                blocked.ShouldBe(expected, "the concurrent requests never waited for the lock on the version");
            }

            blocked = (long)(await command.ExecuteScalarAsync(cancellationToken) ?? 0L);
            if (blocked < expected)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);
            }
        }

        blocked.ShouldBe(expected);
    }
}
