using System.Reflection;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.EventStore;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Modules;
using JasperFx;
using Marten;
using Microsoft.Extensions.Configuration;

namespace Microsoft.Extensions.DependencyInjection;

public static class EventStoreRegistration
{
    /// <summary>
    /// Gives the module its own Marten store when its Infrastructure project has adapters over one: classes whose
    /// constructor takes an <see cref="IDocumentStore"/> (among other services). Each adapter is registered as the module's Application
    /// interfaces it implements (I&lt;Module&gt;AggregateStore, ...). The store never changes the database's schema; the
    /// MigrationRunner applies it (<see cref="ApplyEventStoreSchemasAsync"/>).
    /// </summary>
    public static IServiceCollection AddModuleEventStore<TModule>(this IServiceCollection services, IConfiguration configuration)
        where TModule : IModule
    {
        Ensure.That(configuration).IsNotNull();

        var module = ModuleEventStore.Of(ModuleAssemblies.Of<TModule>("Infrastructure"));
        if (module is null)
        {
            return services;
        }

        var connectionString = ModuleEventStore.ConnectionString(configuration);
        var key = typeof(TModule).FullName!;
        services.AddKeyedSingleton<IDocumentStore>(key, (_, _) => DocumentStore.For(options => module.Configure(options, connectionString, AutoCreate.None)));

        var application = ModuleAssemblies.Of<TModule>("Application");
        foreach (var adapter in module.Adapters)
        {
            foreach (var contract in adapter.GetInterfaces().Where(contract => contract.Assembly == application))
            {
                services.AddScoped(contract, provider => ActivatorUtilities.CreateInstance(provider, adapter, provider.GetRequiredKeyedService<IDocumentStore>(key)));
            }
        }

        return services;
    }

    /// <summary>
    /// Creates or updates the tables of one module's event store, in its service's own database: streams, events and the
    /// projections' read models. Nothing when the module has no event store.
    /// </summary>
    public static async Task ApplyEventStoreSchemasAsync(this IServiceProvider services, Assembly infrastructure)
    {
        Ensure.That(services).IsNotNull();
        Ensure.That(infrastructure).IsNotNull();

        if (ModuleEventStore.Of(infrastructure) is not { } module)
        {
            return;
        }

        var connectionString = ModuleEventStore.ConnectionString(services.GetRequiredService<IConfiguration>());
        using var store = DocumentStore.For(options => module.Configure(options, connectionString, AutoCreate.CreateOrUpdate));
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
    }
}
