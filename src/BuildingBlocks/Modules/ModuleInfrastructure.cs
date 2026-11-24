using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Modules;

public static class ModuleInfrastructure
{
    /// <summary>Everything a module needs from infrastructure; each choice that needs something per module adds it here.</summary>
    public static IServiceCollection AddModuleInfrastructure<TModule>(this IServiceCollection services, IConfiguration configuration)
        where TModule : IModule
    {
        Ensure.That(services).IsNotNull();
        Ensure.That(configuration).IsNotNull();
        services.AddModuleDbContext<TModule>(configuration);
        services.AddDatabaseErrorDetailGuard(configuration);
        services.AddModuleHandlers<TModule>();
        services.AddModuleUnitOfWork<TModule>();
        services.AddModuleEventStore<TModule>(configuration);
        services.AddModuleDomainEventReactions<TModule>();
        services.AddModuleAdapters<TModule>();
        services.AddModulePolicies<TModule>();
        services.AddModuleBlobStorage(configuration);
        return services;
    }
}
