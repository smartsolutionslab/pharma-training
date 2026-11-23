using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure;

namespace Aspire.Hosting;

/// <summary>
/// The one storage account every service gets a blob container of its own on (docs/adr/0002): Azurite with a data volume
/// in run mode; published, an Azure storage account with shared-key access off. Aspire's default roles would make every
/// service that references a container a Blob, Table and Queue Data Contributor on the whole account; published, there
/// are none, and each service is granted its own container alone (<see cref="BlobContainerAccess"/>).
/// </summary>
public static class BlobStorage
{
    private const string Name = "storage";

    public static IResourceBuilder<AzureStorageResource> AddBlobStorage(this IDistributedApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var storage = builder.AddAzureStorage(Name)
            .RunAsEmulator(emulator => emulator.WithDataVolume());
        return builder.ExecutionContext.IsPublishMode ? storage.ClearDefaultRoleAssignments() : storage;
    }
}
