using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Persistence;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Microsoft.Extensions.DependencyInjection;

public static class DatabaseErrorDetailRegistration
{
    /// <summary>Registers, once however many modules ask, the check that refuses error detail outside Development.</summary>
    public static IServiceCollection AddDatabaseErrorDetailGuard(this IServiceCollection services)
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, DatabaseErrorDetailGuard>());
        return services;
    }
}
