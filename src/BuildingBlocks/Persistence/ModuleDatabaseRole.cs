using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Persistence;

/// <summary>
/// The database role a module's service logs in as once published, never as the server's administrator (OQ8 c on #114,
/// docs/adr/0005). The MigrationRunner ensures it after the migrations, logged in as the administrator: the role named by
/// the <c>application</c> connection string's user, with its password, may log in and nothing more, may connect to its
/// own database alone, may read and write what the administrator made there (every schema the administrator owns, later
/// tables too), and may create schemas of its own in that database, where Wolverine builds its message storage at start.
/// It never changes the module's schema. Every run is idempotent and re-applies everything, so a new password or a new
/// table is picked up by the next one; runs on one database wait for each other. It may never change EF Core's migration
/// history. The session resolves names in pg_catalog alone (<see cref="AdministratorSession"/>), the role's password
/// reaches the server only as a SCRAM-SHA-256 verifier (<see cref="ScramSha256Verifier"/>), and a database grant that
/// grants nothing fails the run. It also revokes CONNECT on the server's maintenance database from PUBLIC, where a revoke
/// that changed nothing is a warning alone (<see cref="MaintenanceDatabase"/>).
/// </summary>
public static class ModuleDatabaseRole
{
    // Transaction-local settings the statement below reads: the role's name and its password's verifier reach the server as
    // bind parameters and are quoted there (format %I and %L), never spliced into SQL here, and never logged as statement text.
    private const string RoleSetting = "pharma_training.module_role";

    private const string VerifierSetting = "pharma_training.module_role_verifier";

    // EF Core's, as PersistenceRegistration names it in every module's schema.
    private const string MigrationsHistory = "__EFMigrationsHistory";

    // Every function and catalog the statement names is schema-qualified, besides the session's pinned search_path
    // (AdministratorSession): the role holds CREATE on the database and could otherwise shadow them (CVE-2018-1058).
    private const string EnsureRole =
        $$"""
        DO $ensure$
        DECLARE
            role_name text := pg_catalog.current_setting('{{RoleSetting}}');
            role_verifier text := pg_catalog.current_setting('{{VerifierSetting}}');
            database_name text := pg_catalog.current_database();
            schema_name name;
            failure_state text;
            failure_message text;
            failure_detail text;
            failure_hint text;
        BEGIN
            -- The statement holds the verifier, never the password; a failure is raised again without the statement as its
            -- context, so the verifier does not reach the client's exception or a log either.
            BEGIN
                IF EXISTS (SELECT FROM pg_catalog.pg_roles WHERE rolname = role_name) THEN
                    -- Only a superuser may change SUPERUSER, REPLICATION or BYPASSRLS, which a role this created never has.
                    EXECUTE pg_catalog.format('ALTER ROLE %I WITH LOGIN NOCREATEDB NOCREATEROLE INHERIT PASSWORD %L', role_name, role_verifier);
                ELSE
                    EXECUTE pg_catalog.format('CREATE ROLE %I WITH LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS INHERIT PASSWORD %L', role_name, role_verifier);
                END IF;
            EXCEPTION WHEN OTHERS THEN
                GET STACKED DIAGNOSTICS
                    failure_state = RETURNED_SQLSTATE,
                    failure_message = MESSAGE_TEXT,
                    failure_detail = PG_EXCEPTION_DETAIL,
                    failure_hint = PG_EXCEPTION_HINT;
                RAISE EXCEPTION USING
                    ERRCODE = failure_state,
                    MESSAGE = pg_catalog.format('Creating or altering the role %I failed: %s', role_name, failure_message),
                    DETAIL = failure_detail,
                    HINT = failure_hint;
            END;

            -- No other module's role may connect here; this one may, use temporary tables, and create schemas of its own.
            EXECUTE pg_catalog.format('REVOKE CONNECT ON DATABASE %I FROM PUBLIC', database_name);
            EXECUTE pg_catalog.format('GRANT CONNECT, TEMPORARY, CREATE ON DATABASE %I TO %I', database_name, role_name);

            -- PostgreSQL only warns when an administrator who neither owns the database nor is a member of its owner revokes
            -- or grants on it, and changes nothing; that must fail the run, not leave PUBLIC connected or the role locked out.
            IF NOT pg_catalog.has_database_privilege(role_name::name, database_name, 'CONNECT')
               OR NOT pg_catalog.has_database_privilege(role_name::name, database_name, 'CREATE')
               OR NOT pg_catalog.has_database_privilege(role_name::name, database_name, 'TEMPORARY') THEN
                RAISE EXCEPTION USING
                    ERRCODE = 'insufficient_privilege',
                    MESSAGE = pg_catalog.format('Granting %I CONNECT, TEMPORARY and CREATE on the database %I granted nothing.', role_name, database_name),
                    HINT = 'The administrator must own the database or be a member of its owner.';
            END IF;

            IF EXISTS (
                SELECT FROM pg_catalog.pg_database AS d
                 CROSS JOIN LATERAL pg_catalog.aclexplode(COALESCE(d.datacl, pg_catalog.acldefault('d', d.datdba))) AS acl
                 WHERE d.datname = database_name AND acl.grantee = 0 AND acl.privilege_type = 'CONNECT') THEN
                RAISE EXCEPTION USING
                    ERRCODE = 'insufficient_privilege',
                    MESSAGE = pg_catalog.format('Revoking CONNECT on the database %I from PUBLIC revoked nothing.', database_name),
                    HINT = 'The administrator must own the database or be a member of its owner.';
            END IF;

            -- What the migrations made: every schema the administrator owns, whatever a module declares, but the one named
            -- after the administrator, which the MigrationRunner claims so that no role may take "$user" (AdministratorSchema).
            FOR schema_name IN
                SELECT nspname FROM pg_catalog.pg_namespace
                 WHERE nspowner = (SELECT oid FROM pg_catalog.pg_roles WHERE rolname = CURRENT_USER)
                   AND nspname <> CURRENT_USER
                   AND nspname NOT LIKE 'pg\_%'
                   AND nspname <> 'information_schema'
                 ORDER BY nspname
            LOOP
                EXECUTE pg_catalog.format('GRANT USAGE ON SCHEMA %I TO %I', schema_name, role_name);
                EXECUTE pg_catalog.format('GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA %I TO %I', schema_name, role_name);
                EXECUTE pg_catalog.format('GRANT USAGE, SELECT, UPDATE ON ALL SEQUENCES IN SCHEMA %I TO %I', schema_name, role_name);
                EXECUTE pg_catalog.format('GRANT EXECUTE ON ALL FUNCTIONS IN SCHEMA %I TO %I', schema_name, role_name);
                EXECUTE pg_catalog.format('ALTER DEFAULT PRIVILEGES FOR ROLE CURRENT_USER IN SCHEMA %I GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO %I', schema_name, role_name);
                EXECUTE pg_catalog.format('ALTER DEFAULT PRIVILEGES FOR ROLE CURRENT_USER IN SCHEMA %I GRANT USAGE, SELECT, UPDATE ON SEQUENCES TO %I', schema_name, role_name);
                EXECUTE pg_catalog.format('ALTER DEFAULT PRIVILEGES FOR ROLE CURRENT_USER IN SCHEMA %I GRANT EXECUTE ON FUNCTIONS TO %I', schema_name, role_name);

                -- The role may read EF Core's migration history, never change it: the next run would skip or repeat a
                -- migration. The grants above, and the default privileges on a history table a migration makes, give it
                -- write access first; this runs after the migrations of every run (MigrationRunner), so it is always taken
                -- back before the run ends.
                IF pg_catalog.to_regclass(pg_catalog.format('%I.%I', schema_name, '{{MigrationsHistory}}')) IS NOT NULL THEN
                    EXECUTE pg_catalog.format('REVOKE INSERT, UPDATE, DELETE, TRUNCATE ON TABLE %I.%I FROM %I', schema_name, '{{MigrationsHistory}}', role_name);
                END IF;
            END LOOP;
        END
        $ensure$
        """;

    private const string Settings = $"SELECT pg_catalog.set_config('{RoleSetting}', @role, true), pg_catalog.set_config('{VerifierSetting}', @verifier, true)";

    private const string RoleLock = "SELECT pg_catalog.pg_advisory_xact_lock(pg_catalog.hashtextextended('pharma-training:module-role:' || @role, 0::bigint))";

    /// <summary>
    /// Creates or updates the role <paramref name="application"/> logs in as, in its database, logged in as
    /// <paramref name="administrator"/>; both name the same database.
    /// </summary>
    public static Task EnsureAsync(string administrator, string application, CancellationToken cancellationToken) =>
        EnsureAsync(administrator, application, NullLogger.Instance, cancellationToken);

    /// <summary>
    /// Creates or updates the role <paramref name="application"/> logs in as, in its database, logged in as
    /// <paramref name="administrator"/>; both name the same database. What it could not close, it logs to
    /// <paramref name="logger"/>.
    /// </summary>
    public static async Task EnsureAsync(string administrator, string application, ILogger logger, CancellationToken cancellationToken)
    {
        Ensure.That(logger, nameof(logger)).IsNotNull();
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

        // A new salt each run, so pg_authid changes on every run while what the role may do does not.
        var verifier = ScramSha256Verifier.Of(role.Password!);

        await using var connection = new NpgsqlConnection(AdministratorSession.Pinned(administrator));
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var settings = new NpgsqlCommand(Settings, connection, transaction))
        {
            settings.Parameters.AddWithValue("role", role.Username!);
            settings.Parameters.AddWithValue("verifier", verifier);
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

        await MaintenanceDatabase.CloseToPublicAsync(connection, transaction, logger, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
