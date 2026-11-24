using System.Reflection;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.EventStore;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Modules;
using JasperFx;
using Marten;
using Npgsql;
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
    /// Creates or updates the tables of one module's event store, in its service's own database: streams, events, the
    /// projections' read models and the partial indexes its announcement sweep reads through (#277). Nothing when the
    /// module has no event store.
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

        // After Marten's changes, which make mt_events on a fresh database: Marten writes objects of its own after a
        // module's, so an index of the module's on its event table cannot be one of them.
        if (module.SweptEventIndexes.Count == 0)
        {
            return;
        }

        // Concurrently (#337), so on a large event table the appends of replicas still running are not held up for the
        // build: outside a transaction, and without a command timeout, since the build takes as long as the table needs.
        await using var connection = store.Storage.Database.CreateConnection();
        await connection.OpenAsync();
        foreach (var index in module.SweptEventIndexes)
        {
            await using (var isInvalid = new NpgsqlCommand(index.IsInvalidQuery, connection))
            {
                if (await isInvalid.ExecuteScalarAsync() is true)
                {
                    // A concurrent build that failed left it behind; "if not exists" would keep it, unused, for good.
                    await using var drop = new NpgsqlCommand(index.DropStatement, connection) { CommandTimeout = 0 };
                    await drop.ExecuteNonQueryAsync();
                }
            }

            await using var create = new NpgsqlCommand(index.CreateStatement, connection) { CommandTimeout = 0 };
            await create.ExecuteNonQueryAsync();
        }
    }
}
