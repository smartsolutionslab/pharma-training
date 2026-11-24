using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Persistence;

/// <summary>
/// What a MigrationRunner run does, from its settings (docs/adr/0005). Named a module in <c>Migrations:Module</c>, it
/// migrates that module's database and ensures its role, as it always did; that is the runner's own. Named no module but a
/// schema in <c>Migrations:OwnedSchema</c>, it is the init container of a database that holds no module's migrations,
/// Keycloak's (Q2 on #114): it ensures the role of <c>ConnectionStrings:application</c> and the schema that role owns
/// (<see cref="OwnedSchemaRole"/>), logged in as the administrator (<c>ConnectionStrings:database</c>), one run at a time
/// per database (<see cref="MigrationGate"/>), and nothing else. Named neither, it fails.
/// </summary>
public static class MigrationRun
{
    /// <summary>The setting that names the module whose database a run migrates.</summary>
    public const string ModuleSetting = "Migrations:Module";

    /// <summary>The setting that names the schema whose role a run ensures, where it migrates no module.</summary>
    public const string OwnedSchemaSetting = "Migrations:OwnedSchema";

    private const string AdministratorConnectionString = "database";

    private const string ApplicationConnectionString = "application";

    /// <summary>
    /// Runs the role-only mode where <paramref name="configuration"/> names no module but an owned schema, and answers
    /// whether it ran; a run naming a module is left to the module's migrations.
    /// </summary>
    public static Task<bool> RunOwnedSchemaModeAsync(IConfiguration configuration, CancellationToken cancellationToken) =>
        RunOwnedSchemaModeAsync(configuration, NullLogger.Instance, cancellationToken);

    /// <summary>
    /// Runs the role-only mode where <paramref name="configuration"/> names no module but an owned schema, and answers
    /// whether it ran, logging what it could not close to <paramref name="logger"/>; a run naming a module is left to the
    /// module's migrations.
    /// </summary>
    public static async Task<bool> RunOwnedSchemaModeAsync(IConfiguration configuration, ILogger logger, CancellationToken cancellationToken)
    {
        Ensure.That(configuration, nameof(configuration)).IsNotNull();
        Ensure.That(logger, nameof(logger)).IsNotNull();

        if (!string.IsNullOrWhiteSpace(configuration[ModuleSetting]))
        {
            return false;
        }

        var schema = configuration[OwnedSchemaSetting];
        if (string.IsNullOrWhiteSpace(schema))
        {
            throw new InvalidOperationException(
                $"Neither {ModuleSetting} nor {OwnedSchemaSetting} is set; the AppHost names the module whose database this run migrates, or the schema whose role it ensures.");
        }

        var administrator = AdministratorSession.Pinned(configuration.GetConnectionString(AdministratorConnectionString)
            ?? throw new InvalidOperationException($"Connection string '{AdministratorConnectionString}' is missing; the AppHost gives the administrator's for the database whose schema {schema} this run ensures."));
        var application = configuration.GetConnectionString(ApplicationConnectionString);
        if (string.IsNullOrWhiteSpace(application))
        {
            throw new InvalidOperationException($"Connection string '{ApplicationConnectionString}' is missing; {OwnedSchemaSetting} ensures the role it logs in as.");
        }

        await using (await MigrationGate.EnterAsync(administrator, cancellationToken))
        {
            await OwnedSchemaRole.EnsureAsync(administrator, application, schema, logger, cancellationToken);
        }

        return true;
    }
}
