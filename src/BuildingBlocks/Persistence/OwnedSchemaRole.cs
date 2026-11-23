using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Persistence;

/// <summary>
/// The role a database that holds no module's migrations logs in as once published, and the one schema it owns there:
/// Keycloak's <c>keycloak_app</c> and <c>keycloak</c> (Q2 on #114, docs/adr/0005). Keycloak's init container, the
/// MigrationRunner in its role-only mode (<see cref="MigrationRun"/>), ensures it logged in as the administrator: the role
/// named by the <c>application</c> connection string's user, with its password, may log in and nothing more, may connect
/// to its own database alone and use temporary tables there, but never create a schema; it owns the schema, where Keycloak
/// creates and changes its own tables, and may create nothing in <c>public</c>. No other role may connect to the database.
/// Making the schema the role's takes the right to SET ROLE to it, which an administrator that is no superuser (Azure's
/// Flexible Server) gets with <c>GRANT &lt;role&gt; TO CURRENT_USER WITH INHERIT FALSE, SET TRUE</c>, once: it inherits
/// nothing of the role's. Every run is idempotent: it re-applies the password and the grants and changes no membership
/// once the schema is the role's; a schema of that name another role owns fails the run; runs for one role wait for each
/// other. As in <see cref="ModuleDatabaseRole"/>, the
/// session resolves names in pg_catalog alone (<see cref="AdministratorSession"/>), the password reaches the server only as
/// a SCRAM-SHA-256 verifier (<see cref="ScramSha256Verifier"/>), the role's and the schema's names are quoted by the server,
/// and a grant or revoke that changed nothing fails the run. CONNECT on the server's maintenance database is revoked from
/// PUBLIC too, where a revoke that changed nothing is a warning alone (<see cref="MaintenanceDatabase"/>).
/// </summary>
public static partial class OwnedSchemaRole
{
    // Transaction-local settings the statement below reads, as bind parameters: never spliced into SQL here, never logged.
    private const string RoleSetting = "pharma_training.owned_schema_role";

    private const string VerifierSetting = "pharma_training.owned_schema_role_verifier";

    private const string SchemaSetting = "pharma_training.owned_schema";

    private const string EnsureRole =
        $$"""
        DO $ensure$
        DECLARE
            role_name text := pg_catalog.current_setting('{{RoleSetting}}');
            role_verifier text := pg_catalog.current_setting('{{VerifierSetting}}');
            schema_name text := pg_catalog.current_setting('{{SchemaSetting}}');
            database_name text := pg_catalog.current_database();
            owner_name name;
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
                    -- As for a module's role: an expiry, a connection limit or a setting an operator, or the role on itself,
                    -- set since the last run no longer locks it out.
                    EXECUTE pg_catalog.format('ALTER ROLE %I WITH LOGIN NOCREATEDB NOCREATEROLE INHERIT CONNECTION LIMIT -1 VALID UNTIL %L PASSWORD %L', role_name, 'infinity', role_verifier);
                    EXECUTE pg_catalog.format('ALTER ROLE %I RESET ALL', role_name);
                    EXECUTE pg_catalog.format('ALTER ROLE %I IN DATABASE %I RESET ALL', role_name, database_name);
                ELSE
                    EXECUTE pg_catalog.format('CREATE ROLE %I WITH LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS INHERIT CONNECTION LIMIT -1 VALID UNTIL %L PASSWORD %L', role_name, 'infinity', role_verifier);
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

            -- No other role may connect here; this one may, and use temporary tables, but never create a schema.
            EXECUTE pg_catalog.format('REVOKE CONNECT ON DATABASE %I FROM PUBLIC', database_name);
            EXECUTE pg_catalog.format('REVOKE CREATE ON DATABASE %I FROM %I', database_name, role_name);
            EXECUTE pg_catalog.format('GRANT CONNECT, TEMPORARY ON DATABASE %I TO %I', database_name, role_name);

            -- PostgreSQL only warns when an administrator who neither owns the database nor is a member of its owner revokes
            -- or grants on it, and changes nothing; that must fail the run, not leave PUBLIC connected or the role locked out.
            IF NOT pg_catalog.has_database_privilege(role_name::name, database_name, 'CONNECT')
               OR NOT pg_catalog.has_database_privilege(role_name::name, database_name, 'TEMPORARY') THEN
                RAISE EXCEPTION USING
                    ERRCODE = 'insufficient_privilege',
                    MESSAGE = pg_catalog.format('Granting %I CONNECT and TEMPORARY on the database %I granted nothing.', role_name, database_name),
                    HINT = 'The administrator must own the database or be a member of its owner.';
            END IF;

            IF pg_catalog.has_database_privilege(role_name::name, database_name, 'CREATE') THEN
                RAISE EXCEPTION USING
                    ERRCODE = 'insufficient_privilege',
                    MESSAGE = pg_catalog.format('%I may still create schemas in the database %I.', role_name, database_name),
                    HINT = 'Revoke CREATE on the database from PUBLIC and from every role it is a member of.';
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

            -- The schema is the role's. Making it so takes the right to SET ROLE to it (PostgreSQL 16 and later), granted to
            -- the administrator once, inheriting nothing; once the schema is the role's, nothing here changes a membership.
            -- A schema of that name some other role owns fails the run: its tables are not the role's to take over.
            SELECT pg_catalog.pg_get_userbyid(nspowner) INTO owner_name FROM pg_catalog.pg_namespace WHERE nspname = schema_name;
            IF owner_name IS NULL THEN
                IF NOT pg_catalog.pg_has_role(CURRENT_USER, role_name::name, 'SET') THEN
                    EXECUTE pg_catalog.format('GRANT %I TO CURRENT_USER WITH INHERIT FALSE, SET TRUE', role_name);
                END IF;

                EXECUTE pg_catalog.format('CREATE SCHEMA %I AUTHORIZATION %I', schema_name, role_name);
            ELSIF owner_name <> role_name THEN
                RAISE EXCEPTION USING
                    ERRCODE = 'insufficient_privilege',
                    MESSAGE = pg_catalog.format('The schema %I belongs to %I, not to %I.', schema_name, owner_name, role_name),
                    HINT = 'Another role created it: inspect what it holds, change its owner or drop it, and run the migrations again.';
            END IF;
        END
        $ensure$
        """;

    private const string Settings =
        $"SELECT pg_catalog.set_config('{RoleSetting}', @role, true), pg_catalog.set_config('{VerifierSetting}', @verifier, true), pg_catalog.set_config('{SchemaSetting}', @schema, true)";

    private const string RoleLock = "SELECT pg_catalog.pg_advisory_xact_lock(pg_catalog.hashtextextended('pharma-training:owned-schema-role:' || @role, 0::bigint))";

    /// <summary>
    /// Creates or updates the role <paramref name="application"/> logs in as and makes <paramref name="schema"/> exist in its
    /// database, owned by it, logged in as <paramref name="administrator"/>; both name the same database.
    /// </summary>
    public static Task EnsureAsync(string administrator, string application, string schema, CancellationToken cancellationToken) =>
        EnsureAsync(administrator, application, schema, NullLogger.Instance, cancellationToken);

    /// <summary>
    /// Creates or updates the role <paramref name="application"/> logs in as and makes <paramref name="schema"/> exist in its
    /// database, owned by it, logged in as <paramref name="administrator"/>; both name the same database. What it could not
    /// close, it logs to <paramref name="logger"/>.
    /// </summary>
    public static async Task EnsureAsync(string administrator, string application, string schema, ILogger logger, CancellationToken cancellationToken)
    {
        Ensure.That(administrator, nameof(administrator)).IsNotNullOrWhiteSpace();
        Ensure.That(application, nameof(application)).IsNotNullOrWhiteSpace();
        Ensure.That(schema, nameof(schema)).IsNotNullOrWhiteSpace().Matches(SchemaName(), "The schema must be a lower-case identifier of at most 63 characters, not starting with pg_.");
        Ensure.That(logger, nameof(logger)).IsNotNull();

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
            settings.Parameters.AddWithValue("schema", schema);
            await settings.ExecuteNonQueryAsync(cancellationToken);
        }

        // Two init containers of one app may start together: the second waits here until the first has committed, without a
        // command timeout, however long that takes; cancellation still ends the wait.
        await using (var gate = new NpgsqlCommand(RoleLock, connection, transaction))
        {
            gate.CommandTimeout = 0;
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

    [GeneratedRegex("^(?!pg_)[a-z_][a-z0-9_]{0,62}$")]
    private static partial Regex SchemaName();
}
