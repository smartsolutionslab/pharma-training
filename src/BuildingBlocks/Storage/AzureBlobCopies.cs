using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Microsoft.Extensions.Configuration;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Storage;

/// <summary>
/// Copies a blob into the module's own container, the connection string <c>blobs</c> (docs/adr/0002), with Put Blob From
/// URL: synchronous, read by the storage service itself from the source URL, for sources up to 5000 MiB on Azure. Only a
/// source on the module's own blob service (same scheme, host and port) is copied; any other is refused before a request
/// is made. The target's name is the module's to derive, so a blob already under it counts as copied: a retry, a second
/// delivery or a lost race end in <see cref="BlobCopyOutcome.AlreadyThere"/> without copying again. The container is read
/// when the first copy is made, so a host without it starts and fails only where a copy is needed.
/// </summary>
public sealed class AzureBlobCopies : IBlobCopies
{
    // A large video is read by the storage service in one request; the SDK's default of 100 seconds would cut it off.
    private static readonly TimeSpan NetworkTimeout = TimeSpan.FromMinutes(10);

    private readonly Lazy<ModuleBlobLocation> location;

    private readonly Lazy<BlobContainerClient> container;

    public AzureBlobCopies(IConfiguration configuration)
    {
        Ensure.That(configuration).IsNotNull();
        location = new Lazy<ModuleBlobLocation>(() => ModuleBlobContainer.Locate(configuration));
        container = new Lazy<BlobContainerClient>(() =>
        {
            var options = new BlobClientOptions();
            options.Retry.NetworkTimeout = NetworkTimeout;
            return ModuleBlobContainer.From(configuration, options);
        });
    }

    public async Task<BlobCopyOutcome> CopyFromAsync(BlobUrl source, BlobName target, CancellationToken cancellationToken)
    {
        Ensure.That(source).IsNotNull();
        Ensure.That(target).IsNotNull();

        var account = location.Value.ServiceUri.GetLeftPart(UriPartial.Authority);
        Ensure.That(source.Url.GetLeftPart(UriPartial.Authority), nameof(source))
            .Satisfies(
                origin => string.Equals(origin, account, StringComparison.OrdinalIgnoreCase),
                "must be a URL on the module's own blob service (same scheme, host and port).");

        var blob = container.Value.GetBlockBlobClient(target.Value);
        try
        {
            await blob.GetPropertiesAsync(cancellationToken: cancellationToken);
            return BlobCopyOutcome.AlreadyThere;
        }
        catch (RequestFailedException failed) when (failed.ErrorCode == BlobErrorCode.BlobNotFound)
        {
            // Not there yet: copy it below.
        }

        try
        {
            await blob.SyncUploadFromUriAsync(source.Url, overwrite: false, cancellationToken);
            return BlobCopyOutcome.Copied;
        }
        catch (RequestFailedException failed) when (failed.ErrorCode == BlobErrorCode.BlobAlreadyExists)
        {
            return BlobCopyOutcome.AlreadyThere;
        }
    }
}
