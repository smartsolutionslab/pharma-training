using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Microsoft.Extensions.DependencyInjection;

public static class DatabaseErrorDetailRegistration
{
    /// <summary>Registers, once however many modules ask, the check that refuses error detail outside Development.</summary>
    public static IServiceCollection AddDatabaseErrorDetailGuard(this IServiceCollection services, IConfiguration configuration)
    {
        Ensure.That(configuration).IsNotNull();
        var connectionString = configuration.GetConnectionString("database");
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, DatabaseErrorDetailGuard>(
            provider => new DatabaseErrorDetailGuard(connectionString, provider.GetRequiredService<IHostEnvironment>())));
        return services;
    }
}
