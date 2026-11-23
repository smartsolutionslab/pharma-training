using Aspire.Hosting.Testing;
using JasperFx;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Persistence;
using Wolverine;
using Wolverine.Persistence.Durability;
using Wolverine.Postgresql;
using Wolverine.Runtime;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// The database role a module's service logs in as once published (OQ8 c on #114, docs/adr/0005): the migration init
/// container, logged in as the administrator, creates it from the <c>application</c> connection string's user and password
/// and grants it what the service needs in its own database alone: reading and writing what the migrations made, Wolverine's
/// storage included, which the runner builds as the administrator, never changing the module's schema or creating a schema
/// of its own. Run against the Aspire PostgreSQL, in databases and under roles each test makes and drops again.
/// </summary>
public sealed class ModuleDatabaseRoleTests(AspireFixture aspire) : IAsyncDisposable
{
    private const string InvalidPassword = "28P01";

    private const string InsufficientPrivilege = "42501";

    private const string Schema = "probe";

    // Any module: the runner migrates it into the test's own database, then builds Wolverine's storage beside it.
    private const string RunnerModule = "catalog";

    private const string Wolverine = "wolverine";

    private readonly List<string> databases = [];

    private readonly List<string> roles = [];

    [Fact]
    public async Task Creates_the_role_that_the_application_connection_string_names()
    {
        var administrator = await DatabaseAsync();
        var application = Application(administrator);

        await ModuleDatabaseRole.EnsureAsync(administrator, application, TestContext.Current.CancellationToken);

        var role = await RoleAsync(administrator, Username(application));
        role.ShouldNotBeNull($"There is no role {Username(application)}.");
        role.ShouldBe(new DatabaseRoleAttributes(CanLogin: true, Superuser: false, CreateDatabase: false, CreateRole: false, Replication: false, BypassRowLevelSecurity: false, Inherit: true));
        (await LoginAsync(application)).ShouldBe(Username(application));
    }

    [Fact]
    public async Task Running_it_again_changes_nothing()
    {
        var administrator = await DatabaseAsync();
        var application = Application(administrator);
        await ExecuteAsync(administrator, $"CREATE SCHEMA {Schema}; CREATE TABLE {Schema}.items (id bigserial PRIMARY KEY, name text NOT NULL)");
        await ModuleDatabaseRole.EnsureAsync(administrator, application, TestContext.Current.CancellationToken);
        var role = await RoleAsync(administrator, Username(application));
        role.ShouldNotBeNull($"There is no role {Username(application)} after the first run.");
        var before = await PrivilegesAsync(administrator, Username(application));

        await ModuleDatabaseRole.EnsureAsync(administrator, application, TestContext.Current.CancellationToken);

        before.ShouldNotBeEmpty($"{Username(application)} holds no privilege in its database.");
        (await RoleAsync(administrator, Username(application))).ShouldBe(role);
        (await PrivilegesAsync(administrator, Username(application))).ShouldBe(before);
        (await LoginAsync(application)).ShouldBe(Username(application));
    }

    [Fact]
    public async Task A_new_password_replaces_the_old()
    {
        var administrator = await DatabaseAsync();
        var old = Application(administrator);
        var renewed = new NpgsqlConnectionStringBuilder(old) { Password = Guid.CreateVersion7().ToString() }.ConnectionString;
        await ModuleDatabaseRole.EnsureAsync(administrator, old, TestContext.Current.CancellationToken);

        await ModuleDatabaseRole.EnsureAsync(administrator, renewed, TestContext.Current.CancellationToken);

        (await LoginAsync(renewed)).ShouldBe(Username(renewed));
        (await LoginAsync(old)).ShouldBe(Refused(InvalidPassword));
    }

    [Fact]
    public async Task The_role_reads_and_writes_what_the_migrations_made()
    {
        var administrator = await DatabaseAsync();
        var application = Application(administrator);
        await ExecuteAsync(administrator, $"CREATE SCHEMA {Schema}; CREATE TABLE {Schema}.items (id bigserial PRIMARY KEY, name text NOT NULL)");
        await ModuleDatabaseRole.EnsureAsync(administrator, application, TestContext.Current.CancellationToken);
        // A later migration, applied before the init container ensures the role again.
        await ExecuteAsync(administrator, $"CREATE TABLE {Schema}.later (id bigserial PRIMARY KEY, name text NOT NULL)");

        await ModuleDatabaseRole.EnsureAsync(administrator, application, TestContext.Current.CancellationToken);

        foreach (var table in new[] { "items", "later" })
        {
            await Should.NotThrowAsync(
                () => ExecuteAsync(application, $"INSERT INTO {Schema}.{table} (name) VALUES ('first'), ('second')"),
                $"{Username(application)} cannot insert into {Schema}.{table}.");
            await Should.NotThrowAsync(
                () => ExecuteAsync(application, $"UPDATE {Schema}.{table} SET name = 'changed' WHERE name = 'first'"),
                $"{Username(application)} cannot update {Schema}.{table}.");
            await Should.NotThrowAsync(
                () => ExecuteAsync(application, $"DELETE FROM {Schema}.{table} WHERE name = 'second'"),
                $"{Username(application)} cannot delete from {Schema}.{table}.");
            (await ScalarAsync<string>(application, $"SELECT string_agg(name, ',') FROM {Schema}.{table}")).ShouldBe("changed");
        }
    }

    [Theory]
    [InlineData("CREATE TABLE probe.intruder (id integer)")]
    [InlineData("ALTER TABLE probe.items ADD COLUMN intruder text")]
    [InlineData("DROP TABLE probe.items")]
    public async Task The_role_cannot_change_the_modules_schema(string statement)
    {
        var administrator = await DatabaseAsync();
        var application = Application(administrator);
        await ExecuteAsync(administrator, $"CREATE SCHEMA {Schema}; CREATE TABLE {Schema}.items (id bigserial PRIMARY KEY, name text NOT NULL)");

        await ModuleDatabaseRole.EnsureAsync(administrator, application, TestContext.Current.CancellationToken);

        var refused = await Should.ThrowAsync<PostgresException>(() => ExecuteAsync(application, statement));
        refused.SqlState.ShouldBe(InsufficientPrivilege, $"{Username(application)} ran '{statement}' into {refused.SqlState}: {refused.MessageText}");
    }

    /// <summary>
    /// The role owns nothing: with CREATE on its database it could create a schema, own what it made there and attach code
    /// to it that runs as whoever touches it, the administrator included (review of #231). Wolverine's storage is the
    /// runner's (<see cref="The_migration_runner_builds_wolverines_storage_owned_by_the_administrator"/>).
    /// </summary>
    [Fact]
    public async Task The_role_holds_no_create_on_its_database()
    {
        var administrator = await ManagedAdministratorAsync();
        var application = Application(administrator);

        await ModuleDatabaseRole.EnsureAsync(administrator, application, TestContext.Current.CancellationToken);

        (await ScalarAsync<bool>(administrator, $"SELECT has_database_privilege('{Username(application)}', current_database(), 'CREATE')"))
            .ShouldBeFalse($"{Username(application)} holds CREATE on its database, so it may create a schema, own it and attach code to what it made there.");
        var refused = await Should.ThrowAsync<PostgresException>(
            () => ExecuteAsync(application, "CREATE SCHEMA intruder"),
            $"{Username(application)} created a schema in its database.");
        refused.SqlState.ShouldBe(InsufficientPrivilege, $"{Username(application)} ran 'CREATE SCHEMA intruder' into {refused.SqlState}: {refused.MessageText}");
    }

    /// <summary>A role an earlier run granted CREATE (before the review of #231) loses it on the next run.</summary>
    [Fact]
    public async Task Running_it_again_takes_back_a_create_an_earlier_run_granted()
    {
        var administrator = await ManagedAdministratorAsync();
        var application = Application(administrator);
        await ModuleDatabaseRole.EnsureAsync(administrator, application, TestContext.Current.CancellationToken);
        await ExecuteAsync(administrator, $"GRANT CREATE ON DATABASE {Database(administrator)} TO {Username(application)}");

        await ModuleDatabaseRole.EnsureAsync(administrator, application, TestContext.Current.CancellationToken);

        (await ScalarAsync<bool>(administrator, $"SELECT has_database_privilege('{Username(application)}', current_database(), 'CREATE')"))
            .ShouldBeFalse($"{Username(application)} keeps the CREATE on its database an earlier run granted.");
    }

    /// <summary>
    /// The MigrationRunner, as the administrator and after the migrations, builds Wolverine's message storage in the schema
    /// wolverine with the host's own configuration, so the administrator owns the schema and everything in it; the role is
    /// granted what it needs there by the grants on every schema the administrator owns. This replaces
    /// Wolverine_builds_its_storage_under_the_role, which asserted that the role built and owned the schema: the review of
    /// #231 moved that behaviour, it was not weakened to pass.
    /// </summary>
    [Fact]
    public async Task The_migration_runner_builds_wolverines_storage_owned_by_the_administrator()
    {
        var administrator = await ManagedAdministratorAsync();
        var application = Application(administrator);

        await RunTheMigrationRunnerAsync(administrator, application);

        var owners = await OwnersInAsync(administrator, Wolverine);
        owners.ShouldNotBeEmpty($"The MigrationRunner built no Wolverine storage: the schema {Wolverine} holds nothing. It should build it, as the administrator, after the migrations.");
        owners.Keys.ShouldContain($"table {Wolverine}.wolverine_incoming_envelopes", $"The MigrationRunner built no inbox. It built: {string.Join(", ", owners.Keys)}");
        owners.Keys.ShouldContain($"table {Wolverine}.wolverine_outgoing_envelopes", $"The MigrationRunner built no outbox. It built: {string.Join(", ", owners.Keys)}");
        owners.Keys.ShouldContain($"table {Wolverine}.wolverine_dead_letters", $"The MigrationRunner built no dead letters. It built: {string.Join(", ", owners.Keys)}");
        owners.Where(owner => owner.Value != Username(administrator)).Select(owner => $"{owner.Key} is {owner.Value}'s").ShouldBeEmpty(
            $"Everything in the schema {Wolverine} should be {Username(administrator)}'s, the administrator's.");
    }

    /// <summary>
    /// What the reviewer of #231 did as the owner of wolverine.wolverine_dead_letters: attach code that runs as whoever
    /// replays a dead letter (<c>UPDATE ... SET replayable = true</c>), the administrator included. Not the owner, the role
    /// may do none of it.
    /// </summary>
    [Fact]
    public async Task The_role_cannot_attach_code_to_wolverines_dead_letters()
    {
        var administrator = await ManagedAdministratorAsync();
        var application = Application(administrator);
        await RunTheMigrationRunnerAsync(administrator, application);
        string[] statements =
        [
            $"CREATE TRIGGER intruder BEFORE UPDATE ON {Wolverine}.wolverine_dead_letters FOR EACH ROW EXECUTE FUNCTION pg_catalog.suppress_redundant_updates_trigger()",
            $"CREATE POLICY intruder ON {Wolverine}.wolverine_dead_letters USING (true)",
            $"ALTER TABLE {Wolverine}.wolverine_dead_letters ENABLE ROW LEVEL SECURITY",
            $"CREATE RULE intruder AS ON UPDATE TO {Wolverine}.wolverine_dead_letters DO ALSO NOTIFY intruder",
        ];

        List<string> outcomes = [];
        foreach (var statement in statements)
        {
            try
            {
                await ExecuteAsync(application, statement);
                outcomes.Add($"{statement}: ran");
            }
            catch (PostgresException refused) when (refused.SqlState == InsufficientPrivilege)
            {
            }
            catch (PostgresException refused)
            {
                outcomes.Add($"{statement}: {refused.SqlState} {refused.MessageText}");
            }
        }

        outcomes.ShouldBeEmpty($"{Username(application)} should be refused each with {InsufficientPrivilege}, as no owner of {Wolverine}.wolverine_dead_letters.");
    }

    /// <summary>
    /// A service, logged in as its role, starts Wolverine configured as src/Host/Wiring/Messaging.cs configures it: its
    /// storage check at start-up finds what the runner built and changes nothing, and the service stores and reads its
    /// envelopes and replays a dead letter.
    /// </summary>
    [Fact]
    public async Task A_service_under_the_role_works_on_the_storage_the_runner_built()
    {
        var administrator = await ManagedAdministratorAsync();
        var application = Application(administrator);
        await RunTheMigrationRunnerAsync(administrator, application);
        (await ScalarAsync<bool>(administrator, $"SELECT to_regclass('{Wolverine}.wolverine_incoming_envelopes') IS NOT NULL"))
            .ShouldBeTrue("The MigrationRunner built no Wolverine inbox for the service to start on.");
        using var host = WolverineHost(application);

        await Should.NotThrowAsync(() => host.StartAsync(TestContext.Current.CancellationToken), $"Wolverine did not start under {Username(application)} on the storage the runner built.");

        var store = host.Services.GetRequiredService<IWolverineRuntime>().Storage;
        var incoming = ProbeEnvelope(EnvelopeStatus.Incoming);
        await store.Inbox.StoreIncomingAsync(incoming);
        (await store.Admin.AllIncomingAsync()).Select(envelope => envelope.Id).ShouldContain(incoming.Id, $"{Username(application)} cannot read back the envelope it stored in the inbox.");
        var outgoing = ProbeEnvelope(EnvelopeStatus.Outgoing);
        await store.Outbox.StoreOutgoingAsync(outgoing, 0);
        (await store.Admin.AllOutgoingAsync()).Select(envelope => envelope.Id).ShouldContain(outgoing.Id, $"{Username(application)} cannot read back the envelope it stored in the outbox.");
        await Should.NotThrowAsync(
            () => ExecuteAsync(application, $"UPDATE {Wolverine}.wolverine_dead_letters SET replayable = true"),
            $"{Username(application)} cannot replay its dead letters.");
        await host.StopAsync(TestContext.Current.CancellationToken);

        (await ScalarAsync<string>(administrator, $"SELECT pg_get_userbyid(nspowner)::text FROM pg_namespace WHERE nspname = '{Wolverine}'"))
            .ShouldBe(Username(administrator), $"The schema {Wolverine} is no longer the administrator's once the service ran.");
    }

    /// <summary>
    /// The role cannot build what is missing, so a service whose storage lacks a table fails at start-up, loudly, rather
    /// than run without it.
    /// </summary>
    [Fact]
    public async Task A_service_under_the_role_fails_to_start_when_a_table_of_its_storage_is_missing()
    {
        var administrator = await ManagedAdministratorAsync();
        var application = Application(administrator);
        await RunTheMigrationRunnerAsync(administrator, application);
        (await ScalarAsync<bool>(administrator, $"SELECT to_regclass('{Wolverine}.wolverine_dead_letters') IS NOT NULL"))
            .ShouldBeTrue("The MigrationRunner built no Wolverine dead letters table to take away.");
        await ExecuteAsync(administrator, $"DROP TABLE {Wolverine}.wolverine_dead_letters");
        using var host = WolverineHost(application);

        await Should.ThrowAsync<Exception>(
            () => host.StartAsync(TestContext.Current.CancellationToken),
            $"Wolverine started under {Username(application)} although {Wolverine}.wolverine_dead_letters is missing.");
    }

    [Fact]
    public async Task The_role_appends_to_the_event_store_the_admin_made()
    {
        var administrator = await DatabaseAsync();
        var application = Application(administrator);
        using (var migrations = EventStore(administrator, AutoCreate.CreateOrUpdate))
        {
            await migrations.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        }

        await ModuleDatabaseRole.EnsureAsync(administrator, application, TestContext.Current.CancellationToken);

        using var store = EventStore(application, AutoCreate.None);
        await using var session = store.LightweightSession();
        var stream = Guid.CreateVersion7();
        session.Events.StartStream(stream, new RoleProbeRecorded("appended under the role"));
        await Should.NotThrowAsync(() => session.SaveChangesAsync(TestContext.Current.CancellationToken), $"{Username(application)} cannot append to the event store.");
        (await ScalarAsync<long>(administrator, $"SELECT count(*) FROM {Schema}_events.mt_events WHERE stream_id = '{stream}'")).ShouldBe(1);
    }

    [Fact]
    public async Task A_role_cannot_connect_to_another_modules_database()
    {
        var first = await DatabaseAsync();
        var second = await DatabaseAsync();
        var firstApplication = Application(first);
        await ModuleDatabaseRole.EnsureAsync(first, firstApplication, TestContext.Current.CancellationToken);
        await ModuleDatabaseRole.EnsureAsync(second, Application(second), TestContext.Current.CancellationToken);

        var intruder = new NpgsqlConnectionStringBuilder(firstApplication) { Database = new NpgsqlConnectionStringBuilder(second).Database }.ConnectionString;

        (await LoginAsync(firstApplication)).ShouldBe(Username(firstApplication));
        (await LoginAsync(intruder)).ShouldBe(Refused(InsufficientPrivilege));
    }

    /// <summary>
    /// PostgreSQL grants CONNECT on the server's maintenance database, <c>postgres</c>, to PUBLIC, so a module's role could
    /// connect there and read what any role may read (PR-B's security review). Ensuring a module's role revokes it; the
    /// administrator still connects there.
    /// </summary>
    [Fact]
    public async Task A_role_cannot_connect_to_the_servers_maintenance_database()
    {
        var administrator = await DatabaseAsync();
        var application = Application(administrator);

        await ModuleDatabaseRole.EnsureAsync(administrator, application, TestContext.Current.CancellationToken);

        var maintenance = new NpgsqlConnectionStringBuilder(application) { Database = "postgres" }.ConnectionString;
        (await LoginAsync(application)).ShouldBe(Username(application));
        (await LoginAsync(maintenance)).ShouldBe(
            Refused(InsufficientPrivilege),
            $"{Username(application)} connects to the server's maintenance database postgres: CONNECT is still granted to PUBLIC there.");
        (await LoginAsync(new NpgsqlConnectionStringBuilder(administrator) { Database = "postgres" }.ConnectionString))
            .ShouldBe(Username(administrator), "The administrator can no longer connect to the maintenance database.");
    }

    [Fact]
    public async Task Two_runs_at_once_both_succeed()
    {
        var administrator = await DatabaseAsync();
        var application = Application(administrator);
        await ExecuteAsync(administrator, $"CREATE SCHEMA {Schema}; CREATE TABLE {Schema}.items (id bigserial PRIMARY KEY, name text NOT NULL)");

        var runs = Enumerable.Range(0, 2).Select(_ => Task.Run(() => ModuleDatabaseRole.EnsureAsync(administrator, application, TestContext.Current.CancellationToken), TestContext.Current.CancellationToken)).ToList();

        await Should.NotThrowAsync(() => Task.WhenAll(runs), "Two init containers starting together should both succeed.");
        (await LoginAsync(application)).ShouldBe(Username(application));
    }

    [Fact]
    public async Task Code_the_role_plants_on_the_administrators_search_path_never_runs_while_the_role_is_ensured()
    {
        var administrator = await ManagedAdministratorAsync();
        var application = Application(administrator);
        await ModuleDatabaseRole.EnsureAsync(administrator, application, TestContext.Current.CancellationToken);
        var trap = await PlantOnTheAdministratorsSearchPathAsync(administrator, application);

        await Should.NotThrowAsync(
            () => ModuleDatabaseRole.EnsureAsync(administrator, application, TestContext.Current.CancellationToken),
            "Ensuring the role again failed once the role had planted functions in a schema named after the administrator.");

        (await TrapCallsAsync(administrator, trap)).ShouldBe(
            0,
            $"EnsureAsync, logged in as {Username(administrator)}, resolved format() or hashtextextended() to the functions {Username(application)} planted in the schema {Username(administrator)} (first on the administrator's search_path, \"$user\"), and ran them as the administrator. Every administrator session must resolve names in pg_catalog alone (search_path = pg_catalog, pg_temp) or call functions schema-qualified.");
    }

    [Fact]
    public async Task Code_the_role_plants_on_the_administrators_search_path_never_runs_while_the_migration_gate_is_held()
    {
        var administrator = await ManagedAdministratorAsync();
        var application = Application(administrator);
        await ModuleDatabaseRole.EnsureAsync(administrator, application, TestContext.Current.CancellationToken);
        var trap = await PlantOnTheAdministratorsSearchPathAsync(administrator, application);

        // The MigrationRunner's own session, held while it migrates; it calls hashtextextended(text, integer) unqualified.
        var gate = await MigrationGate.EnterAsync(administrator, TestContext.Current.CancellationToken);
        await gate.DisposeAsync();

        (await TrapCallsAsync(administrator, trap)).ShouldBe(
            0,
            $"MigrationGate, logged in as {Username(administrator)}, resolved hashtextextended(text, integer) to the function {Username(application)} planted in the schema {Username(administrator)}, an exact match that wins over pg_catalog's (text, bigint), and ran it as the administrator. The gate's session must resolve names in pg_catalog alone (search_path = pg_catalog, pg_temp) or call its functions schema-qualified with typed arguments.");
    }

    [Fact]
    public async Task A_refused_password_change_never_shows_the_password()
    {
        var administrator = await ManagedAdministratorAsync();
        var application = Application(administrator);
        var server = await ServerAsync();
        // A role the administrator did not create and holds no ADMIN option on: it may not change its password.
        await ExecuteAsync(server, $"CREATE ROLE {Username(application)} WITH LOGIN PASSWORD '{Guid.CreateVersion7()}'");
        var password = new NpgsqlConnectionStringBuilder(application).Password!;

        var refused = await Should.ThrowAsync<Exception>(
            () => ModuleDatabaseRole.EnsureAsync(administrator, application, TestContext.Current.CancellationToken),
            $"{Username(administrator)} holds no ADMIN option on {Username(application)}, so ensuring it should fail.");

        var disclosed = Disclosed(refused);
        disclosed.ShouldNotContain(
            password,
            Case.Sensitive,
            $"The failure shows {Username(application)}'s password in clear; send PostgreSQL a SCRAM-SHA-256 verifier computed in the client, never the password. What the failure disclosed:\n{disclosed}");
    }

    [Fact]
    public async Task The_password_reaches_the_server_as_a_scram_verifier_whatever_the_server_would_hash_it_with()
    {
        var administrator = await ManagedAdministratorAsync();
        var application = Application(administrator);
        var server = await ServerAsync();
        // The server would store a password it is sent in clear as MD5 here; a verifier the client computed it stores as sent.
        await ExecuteAsync(server, $"ALTER DATABASE {new NpgsqlConnectionStringBuilder(administrator).Database} SET password_encryption = 'md5'");

        await ModuleDatabaseRole.EnsureAsync(administrator, application, TestContext.Current.CancellationToken);

        var stored = await ScalarAsync<string>(server, $"SELECT coalesce(rolpassword, '(none)') FROM pg_authid WHERE rolname = '{Username(application)}'");
        stored.ShouldStartWith(
            "SCRAM-SHA-256$",
            Case.Sensitive,
            $"pg_authid stores {Username(application)}'s password as '{stored[..Math.Min(stored.Length, 14)]}…': the server hashed a password it was sent in clear. Send a SCRAM-SHA-256 verifier computed in the client.");
        (await LoginAsync(application)).ShouldBe(Username(application), $"{Username(application)} cannot log in with its password in clear.");
    }

    [Fact]
    public async Task A_database_grant_that_grants_nothing_fails_the_run()
    {
        var server = await ServerAsync();
        var owner = $"probe_{Guid.CreateVersion7():N}_owner";
        await ExecuteAsync(server, $"CREATE ROLE {owner} NOLOGIN");
        roles.Add(owner);
        var administrator = await ManagedAdministratorAsync();
        // The database belongs to a role the administrator is no member of: REVOKE and GRANT on it only warn.
        await ExecuteAsync(server, $"ALTER DATABASE {new NpgsqlConnectionStringBuilder(administrator).Database} OWNER TO {owner}");
        var application = Application(administrator);

        await Should.ThrowAsync<Exception>(
            () => ModuleDatabaseRole.EnsureAsync(administrator, application, TestContext.Current.CancellationToken),
            $"EnsureAsync succeeded although {Username(administrator)} could neither revoke CONNECT from PUBLIC nor grant {Username(application)} CONNECT and TEMPORARY on a database {owner} owns; PostgreSQL only warns (no privileges were granted / could be revoked), and the run must fail on it.");
    }

    /// <summary>
    /// A second runner waits at the gate for as long as the first migrates, however long that is: Npgsql's command timeout
    /// (30 seconds by default, 1 here) must not end the wait.
    /// </summary>
    [Fact]
    public async Task A_second_run_waits_at_the_migration_gate_beyond_the_command_timeout()
    {
        var administrator = await DatabaseAsync();
        var impatient = new NpgsqlConnectionStringBuilder(administrator) { CommandTimeout = 1 }.ConnectionString;
        var first = await MigrationGate.EnterAsync(administrator, TestContext.Current.CancellationToken);
        Task<MigrationGate> second;
        try
        {
            second = MigrationGate.EnterAsync(impatient, TestContext.Current.CancellationToken);
            // Three times the command timeout: the wait has to outlast it.
            await Task.WhenAny(second, Task.Delay(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken));

            second.IsCompleted.ShouldBeFalse($"The second run stopped waiting at the gate while the first held it: {second.Exception?.GetBaseException().Message}");
        }
        finally
        {
            await first.DisposeAsync();
        }

        await Should.NotThrowAsync(
            async () =>
            {
                await using var entered = await second;
            },
            "The second run did not enter the gate once the first left it.");
    }

    /// <summary>
    /// When the run fails because the gate's session is gone (the server ended it, the network dropped), leaving the gate
    /// cannot unlock and must not throw over the run's own failure: that is what the init container's log has to show.
    /// </summary>
    [Fact]
    public async Task Leaving_the_gate_after_its_session_dropped_keeps_the_runs_own_failure()
    {
        var administrator = await DatabaseAsync();
        var failure = new InvalidOperationException("The migration failed: the server closed the connection.");

        var thrown = await Should.ThrowAsync<Exception>(async () =>
        {
            await using (await MigrationGate.EnterAsync(administrator, TestContext.Current.CancellationToken))
            {
                (await TerminateTheGatesSessionAsync(administrator)).ShouldBe(1, "No session held the gate's lock.");
                throw failure;
            }
        });

        thrown.ShouldBeSameAs(failure, $"Leaving the gate replaced the run's failure with its own: {thrown}");
    }

    /// <summary>
    /// Whatever keeps the role from logging in or working, set on it since the last run, by an operator or by the role on
    /// itself, the next run lifts: its password, its expiry, its connection limit and its settings, everywhere and in its
    /// database, are the run's, not what was left.
    /// </summary>
    [Theory]
    [InlineData("VALID UNTIL '2000-01-01'")]
    [InlineData("CONNECTION LIMIT 0")]
    [InlineData("SET statement_timeout = 1")]
    [InlineData("IN DATABASE {database} SET statement_timeout = 1")]
    public async Task Running_it_again_lifts_what_locks_the_role_out(string lockOut)
    {
        var administrator = await ManagedAdministratorAsync();
        var application = Application(administrator);
        await ModuleDatabaseRole.EnsureAsync(administrator, application, TestContext.Current.CancellationToken);
        var alteration = lockOut.Replace("{database}", Database(administrator), StringComparison.Ordinal);
        await ExecuteAsync(administrator, $"ALTER ROLE {Username(application)} {alteration}");
        (await WorkAsync(application)).ShouldNotBe(Username(application), $"ALTER ROLE ... {alteration} did not lock {Username(application)} out; the test proves nothing.");

        await ModuleDatabaseRole.EnsureAsync(administrator, application, TestContext.Current.CancellationToken);

        (await WorkAsync(application)).ShouldBe(Username(application), $"{Username(application)} is still locked out by {alteration} after the run.");
    }

    [Theory]
    [InlineData("INSERT INTO probe.\"__EFMigrationsHistory\" (\"MigrationId\", \"ProductVersion\") VALUES ('29991231000000_Intruder', '10.0.0')")]
    [InlineData("UPDATE probe.\"__EFMigrationsHistory\" SET \"ProductVersion\" = 'intruder'")]
    [InlineData("DELETE FROM probe.\"__EFMigrationsHistory\"")]
    public async Task The_role_cannot_rewrite_the_migration_history(string statement)
    {
        var administrator = await DatabaseAsync();
        var application = Application(administrator);
        // As EF Core makes it for a module (MigrationsHistoryTable("__EFMigrationsHistory", schema)), with one migration applied.
        await ExecuteAsync(
            administrator,
            $"""
            CREATE SCHEMA {Schema};
            CREATE TABLE {Schema}."__EFMigrationsHistory" ("MigrationId" character varying(150) PRIMARY KEY, "ProductVersion" character varying(32) NOT NULL);
            INSERT INTO {Schema}."__EFMigrationsHistory" VALUES ('20261118000000_CreateSamples', '10.0.0');
            CREATE TABLE {Schema}.items (id bigserial PRIMARY KEY, name text NOT NULL)
            """);

        await ModuleDatabaseRole.EnsureAsync(administrator, application, TestContext.Current.CancellationToken);

        var refused = await Should.ThrowAsync<PostgresException>(
            () => ExecuteAsync(application, statement),
            $"{Username(application)} ran '{statement}': it may change EF Core's migration history, so the next run would skip or repeat a migration.");
        refused.SqlState.ShouldBe(InsufficientPrivilege, $"{Username(application)} ran '{statement}' into {refused.SqlState}: {refused.MessageText}");
    }

    public async ValueTask DisposeAsync()
    {
        var server = await ServerAsync();
        foreach (var database in databases)
        {
            await ExecuteAsync(server, $"DROP DATABASE IF EXISTS {database} WITH (FORCE)");
        }

        // Last made, first dropped: a role an administrator of the test's own created goes before that administrator.
        foreach (var role in Enumerable.Reverse(roles))
        {
            await ExecuteAsync(server, $"DROP ROLE IF EXISTS {role}");
        }
    }

    private static string Username(string connectionString) => new NpgsqlConnectionStringBuilder(connectionString).Username!;

    private static string Database(string connectionString) => new NpgsqlConnectionStringBuilder(connectionString).Database!;

    private static string Refused(string sqlState) => $"refused with {sqlState}";

    /// <summary>
    /// Who a connection string logs in as, once a statement that takes a moment has run, or the SQL state the server refused
    /// either with. Unpooled, so every call logs in afresh.
    /// </summary>
    private static async Task<string> WorkAsync(string connectionString)
    {
        try
        {
            return await ScalarAsync<string>(
                new NpgsqlConnectionStringBuilder(connectionString) { Pooling = false }.ConnectionString,
                "SELECT current_user::text FROM pg_sleep(0.05)");
        }
        catch (PostgresException refused)
        {
            return Refused(refused.SqlState);
        }
    }

    /// <summary>Wolverine as src/Host/Wiring/Messaging.cs configures its storage: the schema wolverine, checked at start-up as Wolverine's default.</summary>
    private static IHost WolverineHost(string connectionString)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddWolverine(options =>
        {
            options.ApplicationAssembly = typeof(ModuleDatabaseRoleTests).Assembly;
            options.UseRuntimeCompilation();
            options.Discovery.DisableConventionalDiscovery();
            options.PersistMessagesWithPostgresql(connectionString, Wolverine);
        });
        return builder.Build();
    }

    private static Envelope ProbeEnvelope(EnvelopeStatus status) => new()
    {
        Id = Guid.CreateVersion7(),
        MessageType = "role-probe",
        ContentType = "application/json",
        Data = [123],
        Destination = new Uri("local://role-probe"),
        Status = status,
    };

    /// <summary>The MigrationRunner's run of <see cref="RunnerModule"/> into the administrator's database, as the init container runs it; it must succeed.</summary>
    private static async Task RunTheMigrationRunnerAsync(string administrator, string application)
    {
        var (exitCode, output) = await MigrationRunnerProcess.RunAsync(RunnerModule, administrator, application, TestContext.Current.CancellationToken);
        exitCode.ShouldBe(0, $"The MigrationRunner failed:\n{output}");
    }

    /// <summary>Every schema, relation and function in a schema, by kind and name, with its owner.</summary>
    private static async Task<Dictionary<string, string>> OwnersInAsync(string connectionString, string schema)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT 'schema ' || nspname, pg_get_userbyid(nspowner)::text FROM pg_namespace WHERE nspname = @schema
            UNION ALL
            SELECT CASE c.relkind WHEN 'S' THEN 'sequence ' WHEN 'i' THEN 'index ' WHEN 'v' THEN 'view ' ELSE 'table ' END || n.nspname || '.' || c.relname, pg_get_userbyid(c.relowner)::text
              FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace WHERE n.nspname = @schema
            UNION ALL
            SELECT 'function ' || p.oid::regprocedure::text, pg_get_userbyid(p.proowner)::text
              FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace WHERE n.nspname = @schema
            """,
            connection);
        command.Parameters.AddWithValue("schema", schema);
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        Dictionary<string, string> owners = [];
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            owners[reader.GetString(0)] = reader.GetString(1);
        }

        return owners;
    }

    /// <summary>Who a connection string logs in as, or the SQL state the server refused it with.</summary>
    private static async Task<string> LoginAsync(string connectionString)
    {
        try
        {
            return await ScalarAsync<string>(connectionString, "SELECT current_user");
        }
        catch (PostgresException refused)
        {
            return Refused(refused.SqlState);
        }
    }

    private static DocumentStore EventStore(string connectionString, AutoCreate schemaChanges) =>
        DocumentStore.For(options =>
        {
            options.Connection(connectionString);
            options.DatabaseSchemaName = $"{Schema}_events";
            options.Events.DatabaseSchemaName = $"{Schema}_events";
            options.AutoCreateSchemaObjects = schemaChanges;
            options.Events.AddEventType<RoleProbeRecorded>();
        });

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<T> ScalarAsync<T>(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        return (T)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }

    /// <summary>
    /// Everything a failure tells whoever reads it: every message and stack, and each PostgreSQL error's fields (message,
    /// detail, hint, context, internal query) and the statement and parameters it failed on.
    /// </summary>
    private static string Disclosed(Exception failure)
    {
        List<string> parts = [];
        for (var current = failure; current is not null; current = current.InnerException)
        {
            parts.Add(current.ToString());
            if (current is PostgresException postgres)
            {
                parts.Add($"MessageText: {postgres.MessageText}");
                parts.Add($"Detail: {postgres.Detail}");
                parts.Add($"Hint: {postgres.Hint}");
                parts.Add($"Where: {postgres.Where}");
                parts.Add($"InternalQuery: {postgres.InternalQuery}");
                parts.Add($"Statement: {postgres.BatchCommand?.CommandText}");
                if (postgres.BatchCommand is { } command)
                {
                    foreach (NpgsqlParameter parameter in command.Parameters)
                    {
                        parts.Add($"Parameter {parameter.ParameterName}: {parameter.Value}");
                    }
                }
            }
        }

        return string.Join('\n', parts);
    }

    /// <summary>
    /// What a module role could do with CREATE on its database, which runs before the review of #231 granted it (the test
    /// grants it again here, as such a run left it): as the reviewer of #114 did, a schema named after the
    /// administrator, first on its default search_path ("$user", public), open to PUBLIC, holding functions that shadow
    /// pg_catalog's by an exact match of the arguments the runner passes. Each counts, on a sequence of a schema of the
    /// role's own, every call made as the administrator (a sequence, so a call counts even if its transaction rolls back),
    /// and otherwise answers as pg_catalog's would. Answers the sequence's name.
    /// </summary>
    private static async Task<string> PlantOnTheAdministratorsSearchPathAsync(string administrator, string application)
    {
        var admin = Username(administrator);
        var trap = $"{Username(application)}_trap";
        await ExecuteAsync(administrator, $"GRANT CREATE ON DATABASE {Database(administrator)} TO {Username(application)}");
        await ExecuteAsync(
            application,
            $$"""
            CREATE SCHEMA {{trap}};
            CREATE SEQUENCE {{trap}}.calls;
            GRANT USAGE ON SCHEMA {{trap}} TO PUBLIC;
            GRANT USAGE ON SEQUENCE {{trap}}.calls TO PUBLIC;
            CREATE SCHEMA {{admin}};
            GRANT USAGE ON SCHEMA {{admin}} TO PUBLIC;
            CREATE FUNCTION {{admin}}.format(text, text, text) RETURNS text LANGUAGE plpgsql AS $trojan$
            BEGIN
                IF current_user = '{{admin}}' THEN
                    PERFORM pg_catalog.nextval('{{trap}}.calls');
                END IF;
                RETURN pg_catalog.format($1, $2, $3);
            END
            $trojan$;
            CREATE FUNCTION {{admin}}.hashtextextended(text, integer) RETURNS bigint LANGUAGE plpgsql AS $trojan$
            BEGIN
                IF current_user = '{{admin}}' THEN
                    PERFORM pg_catalog.nextval('{{trap}}.calls');
                END IF;
                RETURN pg_catalog.hashtextextended($1, $2::bigint);
            END
            $trojan$;
            """);
        return $"{trap}.calls";
    }

    private static async Task<DatabaseRoleAttributes?> RoleAsync(string administrator, string role)
    {
        await using var connection = new NpgsqlConnection(administrator);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT rolcanlogin, rolsuper, rolcreatedb, rolcreaterole, rolreplication, rolbypassrls, rolinherit FROM pg_roles WHERE rolname = @role",
            connection);
        command.Parameters.AddWithValue("role", role);
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        if (!await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            return null;
        }

        return new DatabaseRoleAttributes(reader.GetBoolean(0), reader.GetBoolean(1), reader.GetBoolean(2), reader.GetBoolean(3), reader.GetBoolean(4), reader.GetBoolean(5), reader.GetBoolean(6));
    }

    /// <summary>Every privilege the role holds in the database, on it, its schemas, relations and functions, and by default.</summary>
    private static async Task<List<string>> PrivilegesAsync(string administrator, string role)
    {
        await using var connection = new NpgsqlConnection(administrator);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT 'database ' || datname || ' ' || privilege_type || ' ' || grantee::regrole::text
              FROM pg_database, aclexplode(datacl) WHERE datname = current_database() AND grantee <> 0
            UNION ALL
            SELECT 'database ' || datname || ' PUBLIC ' || privilege_type
              FROM pg_database, aclexplode(datacl) WHERE datname = current_database() AND grantee = 0
            UNION ALL
            SELECT 'schema ' || nspname || ' ' || privilege_type
              FROM pg_namespace, aclexplode(nspacl) WHERE grantee = @role::regrole
            UNION ALL
            SELECT 'relation ' || relnamespace::regnamespace::text || '.' || relname || ' ' || privilege_type
              FROM pg_class, aclexplode(relacl) WHERE grantee = @role::regrole
            UNION ALL
            SELECT 'function ' || p.oid::regprocedure::text || ' ' || privilege_type
              FROM pg_proc p, aclexplode(proacl) WHERE grantee = @role::regrole
            UNION ALL
            SELECT 'default ' || defaclobjtype::text || ' in ' || coalesce(defaclnamespace::regnamespace::text, '*') || ' ' || privilege_type
              FROM pg_default_acl, aclexplode(defaclacl) WHERE grantee = @role::regrole
            ORDER BY 1
            """,
            connection);
        command.Parameters.AddWithValue("role", role);
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        List<string> privileges = [];
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            privileges.Add(reader.GetString(0));
        }

        return privileges;
    }

    /// <summary>The server's administrator connection string, Aspire's, without pooling.</summary>
    private async Task<string> ServerAsync()
    {
        var connectionString = await aspire.App.GetConnectionStringAsync("postgres", TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("The PostgreSQL server has no connection string.");
        return new NpgsqlConnectionStringBuilder(connectionString) { Pooling = false }.ConnectionString;
    }

    /// <summary>Ends, as the server's superuser, the session that holds the migration gate's lock in the administrator's database; answers how many it ended.</summary>
    private async Task<long> TerminateTheGatesSessionAsync(string administrator)
    {
        await using var connection = new NpgsqlConnection(await ServerAsync());
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT count(pg_terminate_backend(l.pid))
              FROM pg_locks l JOIN pg_database d ON d.oid = l.database
             WHERE l.locktype = 'advisory' AND l.granted AND d.datname = @database
            """,
            connection);
        command.Parameters.AddWithValue("database", Database(administrator));
        return (long)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }

    /// <summary>How often the planted functions ran as the administrator, read by the server's superuser.</summary>
    private async Task<long> TrapCallsAsync(string administrator, string trap)
    {
        var superuser = new NpgsqlConnectionStringBuilder(await ServerAsync()) { Database = new NpgsqlConnectionStringBuilder(administrator).Database }.ConnectionString;
        return await ScalarAsync<long>(superuser, $"SELECT CASE WHEN is_called THEN last_value ELSE 0 END FROM {trap}");
    }

    /// <summary>
    /// An administrator as Azure Database for PostgreSQL Flexible Server's is: no superuser, CREATEROLE and CREATEDB, owner
    /// of a database of the test's own, which it logs in to; both dropped after the test.
    /// </summary>
    private async Task<string> ManagedAdministratorAsync()
    {
        var server = await ServerAsync();
        var name = $"probe_{Guid.CreateVersion7():N}_admin";
        var password = Guid.CreateVersion7().ToString();
        await ExecuteAsync(server, $"CREATE ROLE {name} WITH LOGIN NOSUPERUSER CREATEROLE CREATEDB PASSWORD '{password}'");
        roles.Add(name);
        var database = $"probe_{Guid.CreateVersion7():N}";
        await ExecuteAsync(server, $"CREATE DATABASE {database} OWNER {name}");
        databases.Add(database);
        return new NpgsqlConnectionStringBuilder(server) { Database = database, Username = name, Password = password }.ConnectionString;
    }

    /// <summary>A database of the test's own, created by the administrator, as a module's is; dropped after the test.</summary>
    private async Task<string> DatabaseAsync()
    {
        var server = await ServerAsync();
        var database = $"probe_{Guid.CreateVersion7():N}";
        await ExecuteAsync(server, $"CREATE DATABASE {database}");
        databases.Add(database);
        return new NpgsqlConnectionStringBuilder(server) { Database = database }.ConnectionString;
    }

    /// <summary>The application connection string the init container is given: the module role's user and password, for the same database.</summary>
    private string Application(string administrator)
    {
        var role = $"probe_{Guid.CreateVersion7():N}_app";
        roles.Add(role);
        return new NpgsqlConnectionStringBuilder(administrator) { Username = role, Password = Guid.CreateVersion7().ToString() }.ConnectionString;
    }
}
