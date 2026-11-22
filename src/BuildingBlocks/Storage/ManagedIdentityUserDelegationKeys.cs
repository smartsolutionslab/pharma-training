using Azure.Core;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Configuration;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Storage;

/// <summary>
/// Asks the storage service for user delegation keys under the service's managed identity, the one <c>AZURE_CLIENT_ID</c>
/// names, which the AppHost gives every published service together with the Storage Blob Delegator role (docs/adr/0002).
/// The identity is read when the first key is asked for, so a host without it starts and fails only where a URL is
/// signed, naming what is missing.
/// </summary>
public sealed class ManagedIdentityUserDelegationKeys : IUserDelegationKeySource
{
    private readonly Lazy<TokenCredential> credential;

    public ManagedIdentityUserDelegationKeys(IConfiguration configuration)
    {
        Ensure.That(configuration).IsNotNull();
        credential = new Lazy<TokenCredential>(() => ServiceIdentity.Credential(configuration));
    }

    public async Task<UserDelegationKey> GetKeyAsync(Uri blobServiceUri, DateTimeOffset expiresOn, CancellationToken cancellationToken)
    {
        Ensure.That(blobServiceUri).IsNotNull();

        var service = new BlobServiceClient(blobServiceUri, credential.Value);
        // No start: the key is valid from the moment the storage service issues it.
        return (await service.GetUserDelegationKeyAsync(startsOn: null, expiresOn, cancellationToken)).Value;
    }
}
