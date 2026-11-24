using Aspire.Hosting.Testing;
using Npgsql;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.EventStore;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Persistence;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// The partial index an announcement sweep reads through (#277, #337, docs/adr/0016) is built concurrently by the
/// MigrationRunner, so appends from the replicas still running during a rolling deploy never wait for it. A concurrent
/// build that fails leaves an invalid index of its name behind, which <c>if not exists</c> would keep for good; the next
/// run drops it and builds it again. Run against the Aspire PostgreSQL, in databases each test makes and drops again,
/// through the MigrationRunner as a service's init container runs it.
/// </summary>
public sealed class SweptEventIndexTests(AspireFixture aspire) : IAsyncDisposable
{
    private readonly List<string> databases = [];

    [Theory]
    [InlineData("learning", "content_completed")]
    [InlineData("assessment", "examination_passed")]
    public async Task Running_the_migration_runner_twice_keeps_the_swept_event_index_valid(string module, string storedType)
    {
        var administrator = await DatabaseAsync();

        await RunTheMigrationRunnerAsync(module, administrator);
        await RunTheMigrationRunnerAsync(module, administrator);

        (await IndexesAsync(administrator, module)).ShouldContain(SweptEventIndex.NameOf(storedType));
        (await IsValidAsync(administrator, module, storedType)).ShouldBe(true, $"{module}.{SweptEventIndex.NameOf(storedType)} is invalid after a second run.");
    }

    [Fact]
    public async Task An_invalid_swept_event_index_a_failed_build_left_behind_is_built_again()
    {
        var administrator = await DatabaseAsync();
        await RunTheMigrationRunnerAsync("learning", administrator);
        // What a concurrent build that failed leaves behind: the index in the catalog, marked invalid, never used.
        await ExecuteAsync(administrator, $"update pg_catalog.pg_index set indisvalid = false where indexrelid = 'learning.{SweptEventIndex.NameOf("content_completed")}'::regclass");
        (await IsValidAsync(administrator, "learning", "content_completed")).ShouldBe(false, "The index is still valid; the test proves nothing.");

        await RunTheMigrationRunnerAsync("learning", administrator);

        (await IsValidAsync(administrator, "learning", "content_completed")).ShouldBe(
            true,
            "The second run kept the invalid index a failed concurrent build left behind; it must drop it and build it again.");
    }

    /// <summary>
    /// A concurrent build waits for every transaction of its database whose snapshot is older than its own, a statement
    /// still running included. A second replica's runner waiting at the gate must not be one, or the first runner's build
    /// waits for the second, which waits for the first to leave the gate: both init containers hang.
    /// </summary>
    [Fact]
    public async Task A_concurrent_index_build_finishes_while_a_second_run_waits_at_the_migration_gate()
    {
        var administrator = await DatabaseAsync();
        await ExecuteAsync(administrator, "create table probe_events (stored_at timestamptz not null)");
        var first = await MigrationGate.EnterAsync(administrator, TestContext.Current.CancellationToken);
        Task<MigrationGate> second;
        try
        {
            second = MigrationGate.EnterAsync(administrator, TestContext.Current.CancellationToken);
            await AnnouncementSweeps.WaitUntilAsync(
                async () => await GateSessionsAsync(administrator) >= 2,
                "The second run did not reach the gate",
                TestContext.Current.CancellationToken);
            second.IsCompleted.ShouldBeFalse("The second run entered the gate while the first held it; the test proves nothing.");

            var impatient = new NpgsqlConnectionStringBuilder(administrator) { CommandTimeout = 15 }.ConnectionString;
            await Should.NotThrowAsync(
                () => ExecuteAsync(impatient, "create index concurrently ix_probe_events_stored_at on probe_events (stored_at)"),
                "A concurrent index build did not finish within 15 s while a second run waited at the migration gate.");
        }
        finally
        {
            await first.DisposeAsync();
        }

        await using var entered = await second;
    }

    /// <summary>
    /// A concurrent build also waits for every session left idle in a transaction that holds a snapshot (#351). Without a
    /// bound it would wait for good, and the new revision's init container would hang without a word; with
    /// <c>lock_timeout</c> on the build's connection the run fails, and its log says which index it was building.
    /// </summary>
    [Fact]
    public async Task A_session_idle_in_a_transaction_fails_the_index_build_after_the_lock_timeout_with_a_log_line()
    {
        var administrator = await DatabaseAsync();
        var index = SweptEventIndex.NameOf("content_completed");
        await RunTheMigrationRunnerAsync("learning", administrator);
        await ExecuteAsync(administrator, $"drop index learning.{index}");
        await using var idle = new NpgsqlConnection(administrator);
        await idle.OpenAsync(TestContext.Current.CancellationToken);
        await using (var snapshot = new NpgsqlCommand("begin isolation level repeatable read; select count(*) from learning.mt_events", idle))
        {
            await snapshot.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        var (exitCode, output) = await MigrationRunnerProcess.RunAsync("learning", administrator, string.Empty, TestContext.Current.CancellationToken);

        exitCode.ShouldNotBe(0, $"The MigrationRunner built {index} although a session held an older snapshot:\n{output}");
        output.ShouldContain($"Building the index {index}", Case.Sensitive, $"The run did not log the build before it waited:\n{output}");
        output.ShouldContain("lock timeout", Case.Insensitive, $"The run did not fail on the lock timeout:\n{output}");
    }

    public async ValueTask DisposeAsync()
    {
        var server = await ServerAsync();
        foreach (var database in databases)
        {
            await ExecuteAsync(server, $"DROP DATABASE IF EXISTS {database} WITH (FORCE)");
        }
    }

    private static async Task RunTheMigrationRunnerAsync(string module, string administrator)
    {
        // No module role: the runner ensures one only when given its connection string.
        var (exitCode, output) = await MigrationRunnerProcess.RunAsync(module, administrator, string.Empty, TestContext.Current.CancellationToken);
        exitCode.ShouldBe(0, $"The MigrationRunner failed for {module}:\n{output}");
    }

    private static async Task<List<string>> IndexesAsync(string connectionString, string schema)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand("select indexname from pg_catalog.pg_indexes where schemaname = @schema and tablename = 'mt_events'", connection);
        command.Parameters.AddWithValue("schema", schema);
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        List<string> indexes = [];
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            indexes.Add(reader.GetString(0));
        }

        return indexes;
    }

    private static async Task<bool> IsValidAsync(string connectionString, string schema, string storedType)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(
            """
            select i.indisvalid
              from pg_catalog.pg_index i
              join pg_catalog.pg_class c on c.oid = i.indexrelid
              join pg_catalog.pg_namespace n on n.oid = c.relnamespace
             where n.nspname = @schema and c.relname = @name
            """,
            connection);
        command.Parameters.AddWithValue("schema", schema);
        command.Parameters.AddWithValue("name", SweptEventIndex.NameOf(storedType));
        return await command.ExecuteScalarAsync(TestContext.Current.CancellationToken) is true;
    }

    /// <summary>The sessions of the database that have asked for the migration gate's lock, the one holding it included.</summary>
    private static async Task<long> GateSessionsAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(
            "select count(*) from pg_catalog.pg_stat_activity where datname = pg_catalog.current_database() and query like '%advisory_lock(%' and pid <> pg_catalog.pg_backend_pid()",
            connection);
        return (long)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>The server's administrator connection string, Aspire's, without pooling.</summary>
    private async Task<string> ServerAsync()
    {
        var connectionString = await aspire.App.GetConnectionStringAsync("postgres", TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("The PostgreSQL server has no connection string.");
        return new NpgsqlConnectionStringBuilder(connectionString) { Pooling = false }.ConnectionString;
    }

    /// <summary>A database of the test's own, as a module's is; dropped after the test.</summary>
    private async Task<string> DatabaseAsync()
    {
        var server = await ServerAsync();
        var database = $"probe_{Guid.CreateVersion7():N}";
        await ExecuteAsync(server, $"CREATE DATABASE {database}");
        databases.Add(database);
        return new NpgsqlConnectionStringBuilder(server) { Database = database }.ConnectionString;
    }
}
