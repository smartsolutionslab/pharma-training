using Microsoft.Extensions.Logging;
using Npgsql;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Persistence;

/// <summary>
/// The server's maintenance database, <c>postgres</c>, to which PostgreSQL grants CONNECT to PUBLIC: without a revoke, a
/// role the MigrationRunner ensures (<see cref="ModuleDatabaseRole"/>, <see cref="OwnedSchemaRole"/>) could connect there and
/// read what any role may read (PR-B's security review, docs/adr/0005). Each run revokes it, then checks whether that took
/// effect. Only the database's owner, a member of its owner, or a superuser can revoke it; on Azure's Flexible Server the
/// administrator may be neither (<c>postgres</c> likely belongs to <c>azure_pg_admin</c>), so a revoke that changed nothing
/// is logged as a warning naming the database, never a failure of the run: a role's own database stays fail-loud.
/// </summary>
public static partial class MaintenanceDatabase
{
    /// <summary>The maintenance database's name.</summary>
    public const string Name = "postgres";

    // Database privileges live in the shared catalog, so this runs from the role's own database. Two init containers of two
    // services may revoke at once, each in its own database, where no advisory lock reaches the other: a concurrent update
    // of the same pg_database row is retried. An administrator with no privilege there at all is refused (42501), which is
    // the same as a revoke that changed nothing. Every name is schema-qualified (AdministratorSession).
    private const string Revoke =
        $$"""
        DO $revoke$
        DECLARE
            attempt integer := 0;
        BEGIN
            IF pg_catalog.current_database() = '{{Name}}'
               OR NOT EXISTS (SELECT FROM pg_catalog.pg_database WHERE datname = '{{Name}}') THEN
                RETURN;
            END IF;

            LOOP
                attempt := attempt + 1;
                BEGIN
                    EXECUTE pg_catalog.format('REVOKE CONNECT ON DATABASE %I FROM PUBLIC', '{{Name}}');
                    EXIT;
                EXCEPTION
                    WHEN insufficient_privilege THEN
                        EXIT;
                    WHEN internal_error OR serialization_failure THEN
                        IF attempt >= 5 THEN
                            EXIT;
                        END IF;

                        PERFORM pg_catalog.pg_sleep(0.2 * attempt);
                END;
            END LOOP;
        END
        $revoke$
        """;

    private const string StillOpen =
        $"""
        SELECT EXISTS (
            SELECT FROM pg_catalog.pg_database AS d
             CROSS JOIN LATERAL pg_catalog.aclexplode(COALESCE(d.datacl, pg_catalog.acldefault('d', d.datdba))) AS acl
             WHERE d.datname = '{Name}' AND acl.grantee = 0 AND acl.privilege_type = 'CONNECT')
        """;

    /// <summary>
    /// Revokes CONNECT on <see cref="Name"/> from PUBLIC on an administrator session, inside its transaction, and logs a
    /// warning when PUBLIC may still connect there.
    /// </summary>
    internal static async Task CloseToPublicAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, ILogger logger, CancellationToken cancellationToken)
    {
        await using (var revoke = new NpgsqlCommand(Revoke, connection, transaction))
        {
            await revoke.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var check = new NpgsqlCommand(StillOpen, connection, transaction);
        if (await check.ExecuteScalarAsync(cancellationToken) is true)
        {
            LogStillOpen(logger, Name);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "PUBLIC may still connect to the server's maintenance database {Database}: the administrator neither owns it nor is a member of its owner, so revoking CONNECT changed nothing. Every role on the server can connect there (docs/adr/0005).")]
    private static partial void LogStillOpen(ILogger logger, string database);
}
