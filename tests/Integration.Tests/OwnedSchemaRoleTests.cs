using Aspire.Hosting.Testing;
using Microsoft.Extensions.Configuration;
using Npgsql;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Persistence;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// The role a database that holds no module's migrations logs in as once published, Keycloak's <c>keycloak_app</c> (Q2 on
/// #114, K6 and K7 of the PR-C plan, docs/adr/0005): Keycloak's init container, the MigrationRunner in its role-only mode,
/// logged in as the administrator, creates it from the <c>application</c> connection string's user and password and makes
/// it the owner of one schema, <c>keycloak</c>, where Keycloak creates and changes its own tables. It may connect to its
/// own database alone and create nothing outside its schema; no other role may connect there. Run against the Aspire
/// PostgreSQL as Azure's Flexible Server runs it: the administrator is no superuser, has CREATEROLE and owns the database.
/// Databases and roles are made and dropped by each test.
/// </summary>
public sealed class OwnedSchemaRoleTests(AspireFixture aspire) : IAsyncDisposable
{
    private const string InvalidPassword = "28P01";

    private const string InsufficientPrivilege = "42501";

    private const string Schema = "keycloak";

    private readonly List<string> databases = [];

    private readonly List<string> roles = [];

    [Fact]
    public async Task Creates_the_role_and_a_schema_it_owns()
    {
        var administrator = await ManagedAdministratorAsync();
        var application = Application(administrator);

        await OwnedSchemaRole.EnsureAsync(administrator, application, Schema, TestContext.Current.CancellationToken);

        var role = await RoleAsync(administrator, Username(application));
        role.ShouldNotBeNull($"There is no role {Username(application)}.");
        role.ShouldBe(new DatabaseRoleAttributes(CanLogin: true, Superuser: false, CreateDatabase: false, CreateRole: false, Replication: false, BypassRowLevelSecurity: false, Inherit: true));
        (await LoginAsync(application)).ShouldBe(Username(application));
        (await SchemaOwnerAsync(administrator, Schema)).ShouldBe(Username(application), $"The schema {Schema} should exist and belong to {Username(application)}.");
    }

    [Fact]
    public async Task The_role_creates_and_alters_tables_in_its_schema_alone()
    {
        var administrator = await ManagedAdministratorAsync();
        var application = Application(administrator);

        await OwnedSchemaRole.EnsureAsync(administrator, application, Schema, TestContext.Current.CancellationToken);

        List<string> problems = [];
        // What Keycloak's Liquibase migrations do in its schema (KC_DB_SCHEMA=keycloak).
        foreach (var statement in new[]
        {
            $"CREATE TABLE {Schema}.realm (id varchar(36) PRIMARY KEY, name varchar(255))",
            $"ALTER TABLE {Schema}.realm ADD COLUMN enabled boolean NOT NULL DEFAULT true",
            $"CREATE INDEX idx_realm_name ON {Schema}.realm (name)",
            $"INSERT INTO {Schema}.realm (id, name) VALUES ('1', 'pharma-training')",
            $"CREATE SEQUENCE {Schema}.hibernate_sequence",
            $"DROP TABLE {Schema}.realm",
        })
        {
            try
            {
                await ExecuteAsync(application, statement);
            }
            catch (PostgresException refused)
            {
                problems.Add($"{Username(application)} could not run '{statement}' in its own schema: {refused.SqlState} {refused.MessageText}");
            }
        }

        foreach (var statement in new[] { "CREATE TABLE public.intruder (id integer)", "CREATE SCHEMA intruder", "CREATE TEMPORARY TABLE scratch (id integer)" })
        {
            var expectedToFail = !statement.StartsWith("CREATE TEMPORARY", StringComparison.Ordinal);
            try
            {
                await ExecuteAsync(application, statement);
                if (expectedToFail)
                {
                    problems.Add($"{Username(application)} ran '{statement}': it may create objects outside its schema.");
                }
            }
            catch (PostgresException refused) when (expectedToFail && refused.SqlState == InsufficientPrivilege)
            {
                // Refused, as it should be.
            }
            catch (PostgresException refused)
            {
                problems.Add($"{Username(application)} ran '{statement}' into {refused.SqlState}: {refused.MessageText}");
            }
        }

        problems.ShouldBeEmpty();
    }

    [Fact]
    public async Task Running_it_again_changes_nothing()
    {
        var administrator = await ManagedAdministratorAsync();
        var application = Application(administrator);
        await OwnedSchemaRole.EnsureAsync(administrator, application, Schema, TestContext.Current.CancellationToken);
        // What Keycloak made on its first start; the next revision's init container ensures the role again.
        await ExecuteAsync(application, $"CREATE TABLE {Schema}.realm (id varchar(36) PRIMARY KEY); INSERT INTO {Schema}.realm VALUES ('1')");
        var role = await RoleAsync(administrator, Username(application));
        role.ShouldNotBeNull($"There is no role {Username(application)} after the first run.");
        var before = await PrivilegesAsync(administrator, Username(application));

        await OwnedSchemaRole.EnsureAsync(administrator, application, Schema, TestContext.Current.CancellationToken);

        (await RoleAsync(administrator, Username(application))).ShouldBe(role);
        (await PrivilegesAsync(administrator, Username(application))).ShouldBe(before);
        (await SchemaOwnerAsync(administrator, Schema)).ShouldBe(Username(application));
        (await ScalarAsync<long>(application, $"SELECT count(*) FROM {Schema}.realm")).ShouldBe(1, "Ensuring the role again lost what Keycloak had stored.");
        (await LoginAsync(application)).ShouldBe(Username(application));
    }

    [Fact]
    public async Task A_new_password_replaces_the_old()
    {
        var administrator = await ManagedAdministratorAsync();
        var old = Application(administrator);
        var renewed = new NpgsqlConnectionStringBuilder(old) { Password = Guid.CreateVersion7().ToString() }.ConnectionString;
        await OwnedSchemaRole.EnsureAsync(administrator, old, Schema, TestContext.Current.CancellationToken);

        await OwnedSchemaRole.EnsureAsync(administrator, renewed, Schema, TestContext.Current.CancellationToken);

        (await LoginAsync(renewed)).ShouldBe(Username(renewed));
        (await LoginAsync(old)).ShouldBe(Refused(InvalidPassword));
    }

    /// <summary>
    /// As for a module's role: whatever keeps Keycloak's role from logging in or working, set on it since the last run, by
    /// an operator or by the role on itself, the next run lifts: its expiry, its connection limit and its settings,
    /// everywhere and in its database.
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
        await OwnedSchemaRole.EnsureAsync(administrator, application, Schema, TestContext.Current.CancellationToken);
        var alteration = lockOut.Replace("{database}", new NpgsqlConnectionStringBuilder(administrator).Database!, StringComparison.Ordinal);
        await ExecuteAsync(administrator, $"ALTER ROLE {Username(application)} {alteration}");
        (await WorkAsync(application)).ShouldNotBe(Username(application), $"ALTER ROLE ... {alteration} did not lock {Username(application)} out; the test proves nothing.");

        await OwnedSchemaRole.EnsureAsync(administrator, application, Schema, TestContext.Current.CancellationToken);

        (await WorkAsync(application)).ShouldBe(Username(application), $"{Username(application)} is still locked out by {alteration} after the run.");
    }

    /// <summary>
    /// PostgreSQL grants CONNECT on a new database to PUBLIC: without a revoke, every module's role could connect to
    /// Keycloak's database. Once the role is ensured, it alone may (PR-B's security review).
    /// </summary>
    [Fact]
    public async Task No_other_role_connects_to_its_database()
    {
        var administrator = await ManagedAdministratorAsync();
        var application = Application(administrator);
        var module = await DatabaseAsync();
        var moduleRole = Application(module);
        await ModuleDatabaseRole.EnsureAsync(module, moduleRole, TestContext.Current.CancellationToken);

        await OwnedSchemaRole.EnsureAsync(administrator, application, Schema, TestContext.Current.CancellationToken);

        var intruder = new NpgsqlConnectionStringBuilder(moduleRole) { Database = new NpgsqlConnectionStringBuilder(administrator).Database }.ConnectionString;
        (await LoginAsync(application)).ShouldBe(Username(application));
        (await LoginAsync(intruder)).ShouldBe(Refused(InsufficientPrivilege), $"The module role {Username(moduleRole)} connects to the database whose schema {Username(application)} owns: CONNECT is still granted to PUBLIC there.");
    }

    [Fact]
    public async Task Two_runs_at_once_both_succeed()
    {
        var administrator = await ManagedAdministratorAsync();
        var application = Application(administrator);

        var runs = Enumerable.Range(0, 2).Select(_ => Task.Run(() => OwnedSchemaRole.EnsureAsync(administrator, application, Schema, TestContext.Current.CancellationToken), TestContext.Current.CancellationToken)).ToList();

        await Should.NotThrowAsync(() => Task.WhenAll(runs), "Two init containers starting together should both succeed.");
        (await LoginAsync(application)).ShouldBe(Username(application));
        (await SchemaOwnerAsync(administrator, Schema)).ShouldBe(Username(application));
    }

    /// <summary>
    /// Every administrator session the runner opens resolves names in pg_catalog alone (AdministratorSession.Pinned, PR-B's
    /// security review), this one too: functions in a schema named after the administrator, first on its default search_path,
    /// that match the arguments of a call better than pg_catalog's never run while the role is ensured.
    /// </summary>
    [Fact]
    public async Task Code_on_the_administrators_search_path_never_runs_while_the_schema_is_ensured()
    {
        var administrator = await ManagedAdministratorAsync();
        var application = Application(administrator);
        var trap = await PlantOnTheAdministratorsSearchPathAsync(administrator);

        await OwnedSchemaRole.EnsureAsync(administrator, application, Schema, TestContext.Current.CancellationToken);

        (await TrapCallsAsync(administrator, trap)).ShouldBe(
            0,
            $"OwnedSchemaRole.EnsureAsync, logged in as {Username(administrator)}, resolved format() or hashtextextended() to functions in the schema {Username(administrator)} (first on the administrator's search_path, \"$user\") and ran them as the administrator. Open its session with AdministratorSession.Pinned and call functions schema-qualified.");
    }

    [Fact]
    public async Task A_run_with_an_owned_schema_and_no_module_ensures_its_role_alone()
    {
        var administrator = await ManagedAdministratorAsync();
        var application = Application(administrator);
        var configuration = Configuration(administrator, application, module: null, ownedSchema: Schema);

        var ran = await MigrationRun.RunOwnedSchemaModeAsync(configuration, TestContext.Current.CancellationToken);

        ran.ShouldBeTrue("Without Migrations:Module, with Migrations:OwnedSchema and ConnectionStrings:application, the run is role-only and should say it ran.");
        (await LoginAsync(application)).ShouldBe(Username(application));
        (await SchemaOwnerAsync(administrator, Schema)).ShouldBe(Username(application));
    }

    [Fact]
    public async Task A_run_naming_a_module_is_left_to_the_modules_migrations()
    {
        var administrator = await ManagedAdministratorAsync();
        var application = Application(administrator);
        var configuration = Configuration(administrator, application, module: "catalog", ownedSchema: null);

        var ran = await MigrationRun.RunOwnedSchemaModeAsync(configuration, TestContext.Current.CancellationToken);

        ran.ShouldBeFalse("A run naming Migrations:Module migrates the module as it always did; the role-only mode should not run.");
        (await RoleAsync(administrator, Username(application))).ShouldBeNull($"The role-only mode created {Username(application)} for a module's run.");
    }

    [Fact]
    public async Task A_run_without_a_module_or_an_owned_schema_fails_naming_both()
    {
        var administrator = await ManagedAdministratorAsync();
        var configuration = Configuration(administrator, Application(administrator), module: null, ownedSchema: null);

        var failure = await Should.ThrowAsync<InvalidOperationException>(() => MigrationRun.RunOwnedSchemaModeAsync(configuration, TestContext.Current.CancellationToken));

        failure.Message.ShouldContain("Migrations:Module", Case.Sensitive, $"The failure does not name Migrations:Module: {failure.Message}");
        failure.Message.ShouldContain("Migrations:OwnedSchema", Case.Sensitive, $"The failure does not name Migrations:OwnedSchema: {failure.Message}");
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

    private static IConfiguration Configuration(string administrator, string application, string? module, string? ownedSchema)
    {
        Dictionary<string, string?> settings = new(StringComparer.Ordinal)
        {
            ["ConnectionStrings:database"] = administrator,
            ["ConnectionStrings:application"] = application,
        };
        if (module is not null)
        {
            settings["Migrations:Module"] = module;
        }

        if (ownedSchema is not null)
        {
            settings["Migrations:OwnedSchema"] = ownedSchema;
        }

        return new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
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

    /// <summary>Who a fresh connection logs in as after a query that takes a moment, or the SQL state the server refused it with.</summary>
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

    /// <summary>The owner of a schema in the administrator's database; null where there is no such schema.</summary>
    private static async Task<string?> SchemaOwnerAsync(string administrator, string schema)
    {
        await using var connection = new NpgsqlConnection(administrator);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand("SELECT pg_get_userbyid(nspowner)::text FROM pg_namespace WHERE nspname = @schema", connection);
        command.Parameters.AddWithValue("schema", schema);
        return await command.ExecuteScalarAsync(TestContext.Current.CancellationToken) as string;
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

    /// <summary>Every privilege on the database, the schemas the role owns or is granted, and the role's memberships.</summary>
    private static async Task<List<string>> PrivilegesAsync(string administrator, string role)
    {
        await using var connection = new NpgsqlConnection(administrator);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT 'database ' || datname || ' ' || privilege_type || ' ' || CASE WHEN grantee = 0 THEN 'PUBLIC' ELSE grantee::regrole::text END
              FROM pg_database, aclexplode(datacl) WHERE datname = current_database()
            UNION ALL
            SELECT 'owns schema ' || nspname FROM pg_namespace WHERE nspowner = @role::regrole
            UNION ALL
            SELECT 'schema ' || nspname || ' ' || privilege_type
              FROM pg_namespace, aclexplode(nspacl) WHERE grantee = @role::regrole
            UNION ALL
            SELECT 'member ' || roleid::regrole::text || ' of ' || member::regrole::text || ' admin ' || admin_option || ' inherit ' || inherit_option || ' set ' || set_option
              FROM pg_auth_members WHERE roleid = @role::regrole OR member = @role::regrole
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

    /// <summary>
    /// A schema named after the administrator, first on its default search_path ("$user", public), open to PUBLIC, holding
    /// functions that shadow pg_catalog's by an exact match of the arguments a statement would pass, each counting every
    /// call made as the administrator on a sequence (so a call counts even if its transaction rolls back) and otherwise
    /// answering as pg_catalog's would. Made by the server's superuser; answers the sequence's name.
    /// </summary>
    private async Task<string> PlantOnTheAdministratorsSearchPathAsync(string administrator)
    {
        var admin = Username(administrator);
        var trap = $"{admin}_trap";
        var superuser = new NpgsqlConnectionStringBuilder(await ServerAsync()) { Database = new NpgsqlConnectionStringBuilder(administrator).Database }.ConnectionString;
        await ExecuteAsync(
            superuser,
            $$"""
            CREATE SCHEMA {{trap}};
            CREATE SEQUENCE {{trap}}.calls;
            GRANT USAGE ON SCHEMA {{trap}} TO PUBLIC;
            GRANT USAGE ON SEQUENCE {{trap}}.calls TO PUBLIC;
            CREATE SCHEMA {{admin}};
            GRANT USAGE ON SCHEMA {{admin}} TO PUBLIC;
            CREATE FUNCTION {{admin}}.format(text, text) RETURNS text LANGUAGE plpgsql AS $trojan$
            BEGIN
                IF current_user = '{{admin}}' THEN
                    PERFORM pg_catalog.nextval('{{trap}}.calls');
                END IF;
                RETURN pg_catalog.format($1, $2);
            END
            $trojan$;
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

    /// <summary>How often the planted functions ran as the administrator, read by the server's superuser.</summary>
    private async Task<long> TrapCallsAsync(string administrator, string trap)
    {
        var superuser = new NpgsqlConnectionStringBuilder(await ServerAsync()) { Database = new NpgsqlConnectionStringBuilder(administrator).Database }.ConnectionString;
        return await ScalarAsync<long>(superuser, $"SELECT CASE WHEN is_called THEN last_value ELSE 0 END FROM {trap}");
    }

    /// <summary>The server's administrator connection string, Aspire's, without pooling.</summary>
    private async Task<string> ServerAsync()
    {
        var connectionString = await aspire.App.GetConnectionStringAsync("postgres", TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("The PostgreSQL server has no connection string.");
        return new NpgsqlConnectionStringBuilder(connectionString) { Pooling = false }.ConnectionString;
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

    /// <summary>A database of the test's own, created by the server's superuser, as a module's is; dropped after the test.</summary>
    private async Task<string> DatabaseAsync()
    {
        var server = await ServerAsync();
        var database = $"probe_{Guid.CreateVersion7():N}";
        await ExecuteAsync(server, $"CREATE DATABASE {database}");
        databases.Add(database);
        return new NpgsqlConnectionStringBuilder(server) { Database = database }.ConnectionString;
    }

    /// <summary>The application connection string the init container is given: the role's user and password, for the same database.</summary>
    private string Application(string administrator)
    {
        var role = $"probe_{Guid.CreateVersion7():N}_app";
        roles.Add(role);
        return new NpgsqlConnectionStringBuilder(administrator) { Username = role, Password = Guid.CreateVersion7().ToString() }.ConnectionString;
    }
}
