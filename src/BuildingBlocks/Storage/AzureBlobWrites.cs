using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Configuration;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Storage;

/// <summary>
/// Creates a blob in the module's own container, the connection string <c>blobs</c> (docs/adr/0002), with Put Blob and
/// <c>If-None-Match: *</c>, so a blob already under the name is never overwritten: the storage service's 409
/// <c>BlobAlreadyExists</c>, or 412 <c>ConditionNotMet</c> for the condition, is <see cref="BlobWriteOutcome.AlreadyThere"/>.
/// Any other failure (a lease, a missing container, a refused identity) is thrown. The container is read when the first
/// blob is written, so a host without it starts and fails only where a write is needed.
/// </summary>
public sealed class AzureBlobWrites : IBlobWrites
{
    private const int Conflict = 409;

    private const int PreconditionFailed = 412;

    private readonly Lazy<BlobContainerClient> container;

    public AzureBlobWrites(IConfiguration configuration)
    {
        Ensure.That(configuration).IsNotNull();
        container = new Lazy<BlobContainerClient>(() => ModuleBlobContainer.From(configuration));
    }

    public async Task<BlobWriteOutcome> CreateAsync(BlobName name, BinaryData content, string contentType, CancellationToken cancellationToken)
    {
        Ensure.That(name).IsNotNull();
        Ensure.That(content).IsNotNull();
        MediaTypes.Guard(contentType, nameof(contentType));

        var options = new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = contentType },
            Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All },
        };
        try
        {
            await container.Value.GetBlobClient(name.Value).UploadAsync(content, options, cancellationToken);
            return BlobWriteOutcome.Created;
        }
        catch (RequestFailedException failed) when ((failed.Status == Conflict && failed.ErrorCode == BlobErrorCode.BlobAlreadyExists)
            || (failed.Status == PreconditionFailed && failed.ErrorCode == BlobErrorCode.ConditionNotMet))
        {
            return BlobWriteOutcome.AlreadyThere;
        }
    }
}
