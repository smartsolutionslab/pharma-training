using Npgsql;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Persistence;

/// <summary>
/// The connection string every session the MigrationRunner opens as a module database's administrator uses (docs/adr/0005).
/// Defence in depth: a module role holds no CREATE on its database, but a role that did (granted by hand, or by a run before
/// the review of #231) could create a schema named after the administrator, which comes first on the default search_path
/// (<c>"$user", public</c>), and put functions or operators there that match the arguments of a call better than
/// pg_catalog's own (<c>format(text, text, text)</c>, <c>hashtextextended(text, integer)</c>). An
/// administrator session that resolved a name there would run the role's code with the administrator's rights: the
/// CVE-2018-1058 pattern. Every administrator session therefore resolves unqualified names in pg_catalog alone, with the
/// session's temporary schema last, as PostgreSQL advises for SECURITY DEFINER functions. Whatever the runner makes, EF
/// Core's migrations and history table, Marten's event store and Wolverine's message storage, names its schema explicitly.
/// </summary>
public static class AdministratorSession
{
    /// <summary>pg_catalog first and only; pg_temp named last, so a temporary object never shadows a catalog one.</summary>
    public const string SearchPath = "pg_catalog, pg_temp";

    /// <summary><paramref name="administrator"/> with its search_path pinned to <see cref="SearchPath"/>.</summary>
    public static string Pinned(string administrator)
    {
        Ensure.That(administrator, nameof(administrator)).IsNotNullOrWhiteSpace();

        return new NpgsqlConnectionStringBuilder(administrator) { SearchPath = SearchPath }.ConnectionString;
    }
}
