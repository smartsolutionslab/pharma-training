using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure;
using Azure.Provisioning;
using Azure.Provisioning.Storage;

namespace Aspire.Hosting;

/// <summary>
/// Gives a service its own blob container as a connection string. Published, the service's identity may read and write
/// that container alone (Storage Blob Data Contributor, scoped to the container) and ask the account for user delegation
/// keys (Storage Blob Delegator, which grants no data access of its own), so it signs its upload and read URLs keyless
/// (docs/adr/0002). The grants are one Bicep module per service, <c>&lt;service&gt;-roles-storage</c>.
/// </summary>
public static class BlobContainerAccess
{
    private const string AccountName = "storage_outputs_name";

    public static IResourceBuilder<T> WithBlobContainer<T>(this IResourceBuilder<T> service, IResourceBuilder<AzureBlobStorageContainerResource> container, string connectionName)
        where T : IResourceWithEnvironment, IComputeResource
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(container);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionName);

        service.WithReference(container, connectionName);
        if (!service.ApplicationBuilder.ExecutionContext.IsPublishMode)
        {
            return service;
        }

        var identity = service.IdentityOf();
        var account = container.Resource.Parent.Parent;
        var containerName = container.Resource.BlobContainerName;
        service.ApplicationBuilder.AddAzureInfrastructure($"{service.Resource.Name}-roles-storage", infrastructure => Grant(infrastructure, containerName))
            .WithParameter(RoleAssignments.PrincipalId, identity.PrincipalId)
            .WithParameter(AccountName, account.NameOutputReference);
        return service;
    }

    private static void Grant(AzureResourceInfrastructure infrastructure, string containerName)
    {
        var principalId = new ProvisioningParameter(RoleAssignments.PrincipalId, typeof(string));
        var accountName = new ProvisioningParameter(AccountName, typeof(string));
        infrastructure.Add(principalId);
        infrastructure.Add(accountName);

        var account = StorageAccount.FromExisting("storage");
        account.Name = accountName;
        var blobs = BlobService.FromExisting("blobs");
        blobs.Parent = account;
        var container = BlobContainer.FromExisting("container");
        container.Name = containerName;
        container.Parent = blobs;
        infrastructure.Add(account);
        infrastructure.Add(blobs);
        infrastructure.Add(container);

        infrastructure.Add(RoleAssignments.Grant(
            container,
            container.Id,
            StorageBuiltInRole.GetBuiltInRoleName(StorageBuiltInRole.StorageBlobDataContributor),
            StorageBuiltInRole.StorageBlobDataContributor.ToString(),
            principalId));
        infrastructure.Add(RoleAssignments.Grant(
            account,
            account.Id,
            StorageBuiltInRole.GetBuiltInRoleName(StorageBuiltInRole.StorageBlobDelegator),
            StorageBuiltInRole.StorageBlobDelegator.ToString(),
            principalId));
    }
}
