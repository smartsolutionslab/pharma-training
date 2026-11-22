using Azure.Storage.Blobs.Models;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Storage;

/// <summary>
/// Where a service gets the user delegation key it signs blob URLs with when its storage account has no account key to
/// sign with (shared-key access off, docs/adr/0002): the storage service itself, asked under the service's own identity.
/// </summary>
public interface IUserDelegationKeySource
{
    /// <summary>A user delegation key for the blob service at <paramref name="blobServiceUri"/>, valid until at least <paramref name="expiresOn"/>.</summary>
    Task<UserDelegationKey> GetKeyAsync(Uri blobServiceUri, DateTimeOffset expiresOn, CancellationToken cancellationToken);
}
