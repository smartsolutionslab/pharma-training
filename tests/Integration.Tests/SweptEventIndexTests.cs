using Aspire.Hosting.Testing;
using Microsoft.Extensions.Logging.Testing;
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
    /// <summary>A transaction that wrote to the event table and was left open: a build waits for it before it starts.</summary>
    private const string OpenWriter = "begin; lock table learning.mt_events in row exclusive mode";

    /// <summary>A transaction that read the event table and was left open: a build waits for its snapshot once built.</summary>
    private const string OlderSnapshot = "begin isolation level repeatable read; select count(*) from learning.mt_events";

    /// <summary>The runner's lock timeout for a test that waits for one, so that it waits a second and not thirty.</summary>
    private static readonly Dictionary<string, string> ShortLockTimeout = new() { [IndexLockTimeout.Setting] = "00:00:01" };

    private readonly List<string> databases = [];

    [Theory]
    [InlineData("learning", "content_completed")]
    [InlineData("assessment", "examination_passed")]
    [InlineData("assessment", "attempt_submitted")]
    [InlineData("assessment", "examination_exhausted")]
    public async Task Running_the_migration_runner_twice_keeps_the_swept_event_index_valid(string module, string storedType)
    {
        var administrator = await DatabaseAsync();

        await RunTheMigrationRunnerAsync(module, administrator);
        await RunTheMigrationRunnerAsync(module, administrator);

        (await IndexesAsync(administrator, module)).ShouldContain(SweptEventIndex.NameOf(storedType));
        (await IsValidAsync(administrator, module, storedType)).ShouldBe(true, $"{module}.{SweptEventIndex.NameOf(storedType)} is invalid after a second run.");
    }

    /// <summary>
    /// The line before a build is there to say why a run waits (#351); a run that finds the index valid builds nothing, so
    /// it says nothing of a build either.
    /// </summary>
    [Fact]
    public async Task A_run_logs_the_index_build_only_when_it_builds_the_index()
    {
        var administrator = await DatabaseAsync();
        var line = $"Building the index {SweptEventIndex.NameOf("content_completed")}";

        var building = await RunTheMigrationRunnerAsync("learning", administrator);
        var valid = await RunTheMigrationRunnerAsync("learning", administrator);

        building.ShouldContain(line, Case.Sensitive, $"The first run built the index without logging it:\n{building}");
        valid.ShouldNotContain(line, Case.Sensitive, $"The second run found the index valid and still logged a build:\n{valid}");
    }

    [Fact]
    public async Task An_invalid_swept_event_index_a_failed_build_left_behind_is_built_again()
    {
        var administrator = await DatabaseAsync();
        await RunTheMigrationRunnerAsync("learning", administrator);
        // What a concurrent build that failed leaves behind: the index in the catalog, marked invalid, never used.
        await ExecuteAsync(administrator, $"update pg_catalog.pg_index set indisvalid = false where indexrelid = 'learning.{SweptEventIndex.NameOf("content_completed")}'::regclass");
        (await IsValidAsync(administrator, "learning", "content_completed")).ShouldBe(false, "The index is still valid; the test proves nothing.");

        var rebuilt = await RunTheMigrationRunnerAsync("learning", administrator);

        rebuilt.ShouldContain($"Building the index {SweptEventIndex.NameOf("content_completed")}", Case.Sensitive, $"The run rebuilt the index without logging it:\n{rebuilt}");
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
    /// <c>lock_timeout</c> on the build's connection the run fails, and its log says which index it was building. The bound
    /// is the runner's setting <c>Migrations:IndexLockTimeout</c> (#359), here a second.
    /// </summary>
    [Fact]
    public async Task A_session_idle_in_a_transaction_fails_the_index_build_after_the_lock_timeout_with_a_log_line()
    {
        var administrator = await DatabaseAsync();
        var index = SweptEventIndex.NameOf("content_completed");
        await RunTheMigrationRunnerAsync("learning", administrator);
        await ExecuteAsync(administrator, $"drop index learning.{index}");
        await using var idle = await IdleInATransactionAsync(administrator, OlderSnapshot);

        var (exitCode, output) = await MigrationRunnerProcess.RunAsync("learning", administrator, string.Empty, ShortLockTimeout, TestContext.Current.CancellationToken);

        exitCode.ShouldNotBe(0, $"The MigrationRunner built {index} although a session held an older snapshot:\n{output}");
        output.ShouldContain($"Building the index {index}", Case.Sensitive, $"The run did not log the build before it waited:\n{output}");
        output.ShouldContain("at most 00:00:01 each", Case.Sensitive, $"The run did not wait for the lock timeout it was given:\n{output}");
        output.ShouldContain("lock timeout", Case.Insensitive, $"The run did not fail on the lock timeout:\n{output}");
    }

    /// <summary>
    /// A build that failed on its lock timeout leaves an invalid index behind (#359): behind a writer left open it fails in
    /// its first wait, before the index is ready for writes (<c>indisready = false</c>); behind an older snapshot it fails
    /// after the build, ready but never valid. Once the session ends, the next run drops it and builds it again.
    /// </summary>
    [Theory]
    [InlineData(OpenWriter, false)]
    [InlineData(OlderSnapshot, true)]
    public async Task A_run_after_a_lock_timeout_builds_the_index_again_once_the_idle_session_ends(string idleTransaction, bool readyAfterTheTimeout)
    {
        var administrator = await DatabaseAsync();
        var index = SweptEventIndex.NameOf("content_completed");
        await RunTheMigrationRunnerAsync("learning", administrator);
        await ExecuteAsync(administrator, $"drop index learning.{index}");
        await using (var idle = await IdleInATransactionAsync(administrator, idleTransaction))
        {
            var (exitCode, output) = await MigrationRunnerProcess.RunAsync("learning", administrator, string.Empty, ShortLockTimeout, TestContext.Current.CancellationToken);
            exitCode.ShouldNotBe(0, $"The MigrationRunner built {index} although a session stood in its way; the test proves nothing:\n{output}");
        }

        (await StateAsync(administrator, "learning", "content_completed")).ShouldBe(
            (Valid: false, Ready: readyAfterTheTimeout),
            "The build that timed out did not leave the invalid index this test recovers from.");

        var rebuilt = await RunTheMigrationRunnerAsync("learning", administrator);

        rebuilt.ShouldContain($"Building the index {index}", Case.Sensitive, $"The run rebuilt the index without logging it:\n{rebuilt}");
        (await IsValidAsync(administrator, "learning", "content_completed")).ShouldBe(
            true,
            "The run after the lock timeout kept the invalid index the failed build left behind; it must drop it and build it again.");
    }

    /// <summary>
    /// A second run waits at the gate on the <see cref="TimeProvider"/> it is given (#351), so a test drives its tries, and
    /// logs once, not per try, that it waits, so an init container's log says why it has not started yet.
    /// </summary>
    [Fact]
    public async Task A_second_run_waits_at_the_migration_gate_on_its_time_provider_and_logs_it_once()
    {
        var administrator = await DatabaseAsync();
        var time = new TimerCountingTimeProvider();
        var logger = new FakeLogger();
        var first = await MigrationGate.EnterAsync(administrator, TestContext.Current.CancellationToken);
        Task<MigrationGate> second;
        try
        {
            second = MigrationGate.EnterAsync(administrator, logger, time, TestContext.Current.CancellationToken);
            for (var tries = 1; tries <= 3; tries++)
            {
                var timers = tries;
                await AnnouncementSweeps.WaitUntilAsync(
                    () => Task.FromResult(time.Timers >= timers),
                    $"The second run did not wait for its try {timers + 1} on the time provider it was given",
                    TestContext.Current.CancellationToken);
                time.Advance(TimeSpan.FromSeconds(1));
            }

            second.IsCompleted.ShouldBeFalse("The second run entered the gate while the first held it.");
        }
        finally
        {
            await first.DisposeAsync();
        }

        await AnnouncementSweeps.WaitUntilAsync(
            () =>
            {
                time.Advance(TimeSpan.FromSeconds(1));
                return Task.FromResult(second.IsCompleted);
            },
            "The second run did not enter the gate once the first had left",
            TestContext.Current.CancellationToken);
        await using var entered = await second;
        logger.Collector.GetSnapshot().Count(record => record.Message.Contains("Waiting for another run at the migration gate", StringComparison.Ordinal))
            .ShouldBe(1, $"The second run must log once that it waits; it logged:\n{string.Join('\n', logger.Collector.GetSnapshot().Select(record => record.Message))}");
    }

    public async ValueTask DisposeAsync()
    {
        var server = await ServerAsync();
        foreach (var database in databases)
        {
            await ExecuteAsync(server, $"DROP DATABASE IF EXISTS {database} WITH (FORCE)");
        }
    }

    /// <summary>Runs the MigrationRunner to a successful end and answers everything it printed.</summary>
    private static async Task<string> RunTheMigrationRunnerAsync(string module, string administrator)
    {
        // No module role: the runner ensures one only when given its connection string.
        var (exitCode, output) = await MigrationRunnerProcess.RunAsync(module, administrator, string.Empty, TestContext.Current.CancellationToken);
        exitCode.ShouldBe(0, $"The MigrationRunner failed for {module}:\n{output}");
        return output;
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

    /// <summary>Whether the index is valid and whether it is ready for writes, as the catalog has it.</summary>
    private static async Task<(bool Valid, bool Ready)> StateAsync(string connectionString, string schema, string storedType)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(
            """
            select i.indisvalid, i.indisready
              from pg_catalog.pg_index i
              join pg_catalog.pg_class c on c.oid = i.indexrelid
              join pg_catalog.pg_namespace n on n.oid = c.relnamespace
             where n.nspname = @schema and c.relname = @name
            """,
            connection);
        command.Parameters.AddWithValue("schema", schema);
        command.Parameters.AddWithValue("name", SweptEventIndex.NameOf(storedType));
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        (await reader.ReadAsync(TestContext.Current.CancellationToken)).ShouldBeTrue($"{schema}.{SweptEventIndex.NameOf(storedType)} does not exist.");
        return (reader.GetBoolean(0), reader.GetBoolean(1));
    }

    /// <summary>A session that ran <paramref name="transaction"/> and stays idle in it until it is disposed.</summary>
    private static async Task<NpgsqlConnection> IdleInATransactionAsync(string connectionString, string transaction)
    {
        var idle = new NpgsqlConnection(connectionString);
        await idle.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(transaction, idle);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        return idle;
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
