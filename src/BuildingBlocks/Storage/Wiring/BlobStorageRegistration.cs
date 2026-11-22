using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Storage;

namespace Microsoft.Extensions.DependencyInjection;

public static class BlobStorageRegistration
{
    /// <summary>
    /// Registers <see cref="IBlobUrls"/> once per host, signing for the module's own container, the connection string
    /// <c>blobs</c> (docs/adr/0002). Registering reads nothing: a host without the connection string starts, and fails only
    /// when it first signs a URL, naming what is missing.
    /// </summary>
    public static IServiceCollection AddModuleBlobStorage(this IServiceCollection services, IConfiguration configuration)
    {
        Ensure.That(services).IsNotNull();
        Ensure.That(configuration).IsNotNull();

        services.TryAddSingleton<IBlobUrls>(provider => new AzureBlobUrls(configuration, provider.GetRequiredService<TimeProvider>()));

        // Red-test stub for #15 (task A1): registered so the module's stubbed handlers resolve, but once per call rather
        // than once per host; B2 registers it with TryAddSingleton.
        services.AddSingleton<IStoredBlobs>(_ => new AzureStoredBlobs(configuration));
        return services;
    }
}
