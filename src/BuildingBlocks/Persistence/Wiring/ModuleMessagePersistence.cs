using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Persistence;
using Wolverine;
using Wolverine.Postgresql;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Where Wolverine keeps a module's envelopes: its inbox, outbox, scheduled messages and dead letters, in the schema
/// <see cref="ModuleMessageStorage.Schema"/> of the module's own database (docs/adr/0005). Every service host configures
/// its storage here (src/Host/Wiring/Messaging.cs), and so does the MigrationRunner, which builds it as the administrator
/// (<see cref="ModuleMessageStorage.BuildAsync"/>): one configuration, so what the runner builds is what the service checks
/// at start-up.
/// </summary>
public static class ModuleMessagePersistence
{
    /// <summary>Keeps Wolverine's envelopes in the module's database named by <paramref name="connectionString"/>.</summary>
    public static WolverineOptions PersistModuleMessages(this WolverineOptions options, string connectionString)
    {
        Ensure.That(options).IsNotNull();
        Ensure.That(connectionString, nameof(connectionString)).IsNotNullOrWhiteSpace();

        options.PersistMessagesWithPostgresql(connectionString, ModuleMessageStorage.Schema);
        return options;
    }
}
