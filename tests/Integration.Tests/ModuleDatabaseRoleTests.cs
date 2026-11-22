using Aspire.Hosting.Testing;
using JasperFx;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Persistence;
using Wolverine;
using Wolverine.Postgresql;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// The database role a module's service logs in as once published (OQ8 c on #114, docs/adr/0005): the migration init
/// container, logged in as the administrator, creates it from the <c>application</c> connection string's user and password
/// and grants it what the service needs in its own database alone: reading and writing what the migrations made, and
/// building Wolverine's storage, never changing the module's schema. Run against the Aspire PostgreSQL, in databases and
/// under roles each test makes and drops again.
/// </summary>
public sealed class ModuleDatabaseRoleTests(AspireFixture aspire) : IAsyncDisposable
{
    private const string InvalidPassword = "28P01";

    private const string InsufficientPrivilege = "42501";

    private const string Schema = "probe";

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

    [Fact]
    public async Task Wolverine_builds_its_storage_under_the_role()
    {
        var administrator = await DatabaseAsync();
        var application = Application(administrator);
        await ModuleDatabaseRole.EnsureAsync(administrator, application, TestContext.Current.CancellationToken);
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddWolverine(options =>
        {
            options.ApplicationAssembly = typeof(ModuleDatabaseRoleTests).Assembly;
            options.UseRuntimeCompilation();
            options.Discovery.DisableConventionalDiscovery();
            // As src/Host/Wiring/Messaging.cs does, the storage built at start-up as Wolverine's default.
            options.PersistMessagesWithPostgresql(application, "wolverine");
        });
        using var host = builder.Build();

        await Should.NotThrowAsync(() => host.StartAsync(TestContext.Current.CancellationToken), $"Wolverine did not start under {Username(application)}.");
        await host.StopAsync(TestContext.Current.CancellationToken);

        (await ScalarAsync<bool>(administrator, "SELECT to_regclass('wolverine.wolverine_incoming_envelopes') IS NOT NULL")).ShouldBeTrue("Wolverine built no inbox.");
        (await ScalarAsync<string>(administrator, "SELECT pg_get_userbyid(nspowner)::text FROM pg_namespace WHERE nspname = 'wolverine'")).ShouldBe(Username(application));
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

    public async ValueTask DisposeAsync()
    {
        var server = await ServerAsync();
        foreach (var database in databases)
        {
            await ExecuteAsync(server, $"DROP DATABASE IF EXISTS {database} WITH (FORCE)");
        }

        foreach (var role in roles)
        {
            await ExecuteAsync(server, $"DROP ROLE IF EXISTS {role}");
        }
    }

    private static string Username(string connectionString) => new NpgsqlConnectionStringBuilder(connectionString).Username!;

    private static string Refused(string sqlState) => $"refused with {sqlState}";

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
