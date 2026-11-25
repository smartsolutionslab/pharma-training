using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure;
using Azure.Provisioning.Storage;

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
        if (!builder.ExecutionContext.IsPublishMode)
        {
            return storage;
        }

        // An upload URL is a create-only SAS (sp=c), which Azure also takes as the destination of Put Blob From URL and Copy
        // Blob: its holder could copy a blob from anywhere into the container instead of uploading it. The account accepts
        // copy sources in its own Entra tenant alone, so a module's own copies (IBlobCopies) still run (#159, docs/adr/0002).
        return storage.ClearDefaultRoleAssignments()
            .ConfigureInfrastructure(infrastructure =>
                infrastructure.GetProvisionableResources().OfType<StorageAccount>().Single().AllowedCopyScope = AllowedCopyScope.Aad);
    }
}
