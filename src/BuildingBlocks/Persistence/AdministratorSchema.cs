using Npgsql;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Persistence;

/// <summary>
/// The schema named after a module database's administrator, which comes first on the administrator's default search_path
/// (<c>"$user", public</c>). A module role holds CREATE on the database, so it could create that schema itself and put
/// functions there that every administrator session not pinned to pg_catalog would run with the administrator's rights:
/// the CVE-2018-1058 pattern. The MigrationRunner's own sessions are pinned (<see cref="AdministratorSession"/>); this
/// protects any other, an operator's psql included. Before the migrations, the MigrationRunner creates the schema, owned by
/// the administrator and empty, so no role can take the name; one some other role already owns fails the run. No role is
/// granted anything in it (<see cref="ModuleDatabaseRole"/>).
/// </summary>
public static class AdministratorSchema
{
    private const string Claim =
        """
        DO $claim$
        DECLARE
            owner_name name;
        BEGIN
            SELECT pg_catalog.pg_get_userbyid(nspowner) INTO owner_name FROM pg_catalog.pg_namespace WHERE nspname = CURRENT_USER;
            IF owner_name IS NULL THEN
                EXECUTE pg_catalog.format('CREATE SCHEMA %I AUTHORIZATION %I', CURRENT_USER, CURRENT_USER);
            ELSIF owner_name <> CURRENT_USER THEN
                RAISE EXCEPTION USING
                    ERRCODE = 'insufficient_privilege',
                    MESSAGE = pg_catalog.format('The schema %I, first on the administrator''s search_path, belongs to %I.', CURRENT_USER, owner_name),
                    HINT = 'Another role created it: inspect what it holds, drop it, and run the migrations again.';
            END IF;
        END
        $claim$
        """;

    /// <summary>Creates the schema named after <paramref name="administrator"/>'s user in its database, unless it owns it already.</summary>
    public static async Task ClaimAsync(string administrator, CancellationToken cancellationToken)
    {
        Ensure.That(administrator, nameof(administrator)).IsNotNullOrWhiteSpace();

        await using var connection = new NpgsqlConnection(AdministratorSession.Pinned(administrator));
        await connection.OpenAsync(cancellationToken);
        await using var claim = new NpgsqlCommand(Claim, connection);
        await claim.ExecuteNonQueryAsync(cancellationToken);
    }
}
