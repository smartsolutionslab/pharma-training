using Npgsql;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Persistence;

/// <summary>
/// The database role a module's service logs in as once published, never as the server's administrator (OQ8 c on #114,
/// docs/adr/0005). The MigrationRunner ensures it after the migrations, logged in as the administrator: the role named by
/// the <c>application</c> connection string's user, with its password, may log in and nothing more, may connect to its
/// own database alone, may read and write what the administrator made there (every schema the administrator owns, later
/// tables too), and may create schemas of its own in that database, where Wolverine builds its message storage at start.
/// It never changes the module's schema. Every run is idempotent and re-applies everything, so a new password or a new
/// table is picked up by the next one; runs on one database wait for each other.
/// </summary>
public static class ModuleDatabaseRole
{
    // Transaction-local settings the statement below reads: the role's name and password reach the server as bind
    // parameters and are quoted there (format %I and %L), never spliced into SQL here, and never logged as statement text.
    private const string RoleSetting = "pharma_training.module_role";

    private const string PasswordSetting = "pharma_training.module_role_password";

    private const string EnsureRole =
        $$"""
        DO $ensure$
        DECLARE
            role_name text := current_setting('{{RoleSetting}}');
            role_password text := current_setting('{{PasswordSetting}}');
            schema_name name;
        BEGIN
            IF EXISTS (SELECT FROM pg_catalog.pg_roles WHERE rolname = role_name) THEN
                -- Only a superuser may change SUPERUSER, REPLICATION or BYPASSRLS, which a role this created never has.
                EXECUTE format('ALTER ROLE %I WITH LOGIN NOCREATEDB NOCREATEROLE INHERIT PASSWORD %L', role_name, role_password);
            ELSE
                EXECUTE format('CREATE ROLE %I WITH LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS INHERIT PASSWORD %L', role_name, role_password);
            END IF;

            -- No other module's role may connect here; this one may, use temporary tables, and create schemas of its own.
            EXECUTE format('REVOKE CONNECT ON DATABASE %I FROM PUBLIC', current_database());
            EXECUTE format('GRANT CONNECT, TEMPORARY, CREATE ON DATABASE %I TO %I', current_database(), role_name);

            -- What the migrations made: every schema the administrator owns, whatever a module declares.
            FOR schema_name IN
                SELECT nspname FROM pg_catalog.pg_namespace
                 WHERE nspowner = (SELECT oid FROM pg_catalog.pg_roles WHERE rolname = current_user)
                   AND nspname NOT LIKE 'pg\_%'
                   AND nspname <> 'information_schema'
                 ORDER BY nspname
            LOOP
                EXECUTE format('GRANT USAGE ON SCHEMA %I TO %I', schema_name, role_name);
                EXECUTE format('GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA %I TO %I', schema_name, role_name);
                EXECUTE format('GRANT USAGE, SELECT, UPDATE ON ALL SEQUENCES IN SCHEMA %I TO %I', schema_name, role_name);
                EXECUTE format('GRANT EXECUTE ON ALL FUNCTIONS IN SCHEMA %I TO %I', schema_name, role_name);
                EXECUTE format('ALTER DEFAULT PRIVILEGES FOR ROLE CURRENT_USER IN SCHEMA %I GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO %I', schema_name, role_name);
                EXECUTE format('ALTER DEFAULT PRIVILEGES FOR ROLE CURRENT_USER IN SCHEMA %I GRANT USAGE, SELECT, UPDATE ON SEQUENCES TO %I', schema_name, role_name);
                EXECUTE format('ALTER DEFAULT PRIVILEGES FOR ROLE CURRENT_USER IN SCHEMA %I GRANT EXECUTE ON FUNCTIONS TO %I', schema_name, role_name);
            END LOOP;
        END
        $ensure$
        """;

    private const string Settings = $"SELECT set_config('{RoleSetting}', @role, true), set_config('{PasswordSetting}', @password, true)";

    private const string RoleLock = "SELECT pg_advisory_xact_lock(hashtextextended('pharma-training:module-role:' || @role, 0))";

    /// <summary>
    /// Creates or updates the role <paramref name="application"/> logs in as, in its database, logged in as
    /// <paramref name="administrator"/>; both name the same database.
    /// </summary>
    public static async Task EnsureAsync(string administrator, string application, CancellationToken cancellationToken)
    {
        Ensure.That(administrator, nameof(administrator)).IsNotNullOrWhiteSpace();
        Ensure.That(application, nameof(application)).IsNotNullOrWhiteSpace();

        var server = new NpgsqlConnectionStringBuilder(administrator);
        var role = new NpgsqlConnectionStringBuilder(application);
        Ensure.That(role.Username ?? string.Empty, nameof(application)).IsNotNullOrWhiteSpace();
        Ensure.That(role.Password ?? string.Empty, nameof(application)).IsNotNullOrWhiteSpace();
        Ensure.That(role.Database ?? string.Empty, nameof(application))
            .Satisfies(database => string.Equals(database, server.Database, StringComparison.Ordinal), "The application connection string names another database than the administrator's.");
        Ensure.That(role.Username!, nameof(application))
            .Satisfies(name => !string.Equals(name, server.Username, StringComparison.Ordinal), "The application connection string logs in as the administrator.");

        await using var connection = new NpgsqlConnection(administrator);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var settings = new NpgsqlCommand(Settings, connection, transaction))
        {
            settings.Parameters.AddWithValue("role", role.Username!);
            settings.Parameters.AddWithValue("password", role.Password!);
            await settings.ExecuteNonQueryAsync(cancellationToken);
        }

        // Two init containers of one service may start together: the second waits here until the first has committed. A key
        // of the role's own, never the MigrationGate's, which the runner holds on another connection meanwhile.
        await using (var gate = new NpgsqlCommand(RoleLock, connection, transaction))
        {
            gate.Parameters.AddWithValue("role", role.Username!);
            await gate.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var ensure = new NpgsqlCommand(EnsureRole, connection, transaction))
        {
            await ensure.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }
}
