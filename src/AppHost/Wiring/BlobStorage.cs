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

    // Put Blob From URL, which a module's server-side copy uses (IBlobCopies), arrived in Azurite 3.38.0; the image Aspire
    // pins by default (3.35.0) answers it 501 APINotImplemented (docs/adr/0002).
    private const string AzuriteTag = "3.38.0";

    public static IResourceBuilder<AzureStorageResource> AddBlobStorage(this IDistributedApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var storage = builder.AddAzureStorage(Name)
            .RunAsEmulator(emulator => emulator.WithDataVolume().WithImageTag(AzuriteTag));
        return builder.ExecutionContext.IsPublishMode ? storage.ClearDefaultRoleAssignments() : storage;
    }
}
