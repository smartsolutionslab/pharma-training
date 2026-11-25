using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Storage;

namespace Microsoft.Extensions.DependencyInjection;

public static class BlobStorageRegistration
{
    /// <summary>
    /// Registers <see cref="IBlobUrls"/>, <see cref="IStoredBlobs"/>, <see cref="IBlobCopies"/>, <see cref="IBlobWrites"/> and <see cref="IBlobReads"/> once per host, for the module's own container, the
    /// connection string <c>blobs</c> (docs/adr/0002). Registering reads nothing: a host without the connection string
    /// starts, and fails only when it first signs a URL, looks up a blob, copies, writes or reads one, naming what is missing. Without an account key
    /// in it (published), URLs are signed with user delegation keys the service's managed identity is issued.
    /// </summary>
    public static IServiceCollection AddModuleBlobStorage(this IServiceCollection services, IConfiguration configuration)
    {
        Ensure.That(services).IsNotNull();
        Ensure.That(configuration).IsNotNull();

        services.TryAddSingleton<IUserDelegationKeySource>(_ => new ManagedIdentityUserDelegationKeys(configuration));
        services.TryAddSingleton<IBlobUrls>(provider => new AzureBlobUrls(configuration, provider.GetRequiredService<TimeProvider>(), provider.GetRequiredService<IUserDelegationKeySource>()));
        services.TryAddSingleton<IStoredBlobs>(_ => new AzureStoredBlobs(configuration));
        services.TryAddSingleton<IBlobCopies>(_ => new AzureBlobCopies(configuration));
        services.TryAddSingleton<IBlobWrites>(_ => new AzureBlobWrites(configuration));
        services.TryAddSingleton<IBlobReads>(_ => new AzureBlobReads(configuration));
        services.TryAddSingleton<IBlobDeletes>(_ => new AzureBlobDeletes(configuration));
        return services;
    }
}
