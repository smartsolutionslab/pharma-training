using Aspire.Hosting.Testing;
using Npgsql;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// What a test does in the editorial database itself: hold a version's row so concurrent requests wait on it, and
/// publish a version before the API can (#18).
/// </summary>
internal static class EditorialDatabase
{
    public static async Task<string> ConnectionStringAsync(AspireFixture aspire, CancellationToken cancellationToken)
    {
        return await aspire.App.GetConnectionStringAsync("editorial-database", cancellationToken)
            ?? throw new InvalidOperationException("The editorial database has no connection string.");
    }

    /// <summary>
    /// Opens a transaction that holds the version's row, so every request that writes the version or anything beneath
    /// it waits after it has loaded the version until the transaction ends. Disposing the gate closes its connection.
    /// </summary>
    public static async Task<VersionLock> LockVersionAsync(string connectionString, Guid id, CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            var transaction = await connection.BeginTransactionAsync(cancellationToken);
            await using (var version = new NpgsqlCommand("SELECT 1 FROM editorial.course_versions WHERE id = @id FOR UPDATE", connection, transaction))
            {
                version.Parameters.AddWithValue("id", id);
                (await version.ExecuteScalarAsync(cancellationToken)).ShouldNotBeNull();
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
    /// Polls the editorial database until the given number of statements on course versions wait for a lock, and
    /// fails the test when they do not within 30 seconds.
    /// </summary>
    public static async Task WaitUntilBlockedAsync(string connectionString, int expected, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT count(*) FROM pg_stat_activity
            WHERE wait_event_type = 'Lock' AND datname = current_database() AND pid <> pg_backend_pid()
              AND query ILIKE '%course_version%'
            """,
            connection);

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

    /// <summary>
    /// Publishing through the API needs submitting and publishing (#18). Until then a test publishes the version in
    /// its own database; #18 replaces this seed with the real submit and publish calls.
    /// </summary>
    public static async Task SeedPublishedAsync(string connectionString, Guid id, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("UPDATE editorial.course_versions SET status = 'Published' WHERE id = @id", connection);
        command.Parameters.AddWithValue("id", id);

        (await command.ExecuteNonQueryAsync(cancellationToken)).ShouldBe(1);
    }
}
