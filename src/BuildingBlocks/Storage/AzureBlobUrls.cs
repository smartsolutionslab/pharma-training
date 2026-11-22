using Azure.Storage.Blobs;
using Azure.Storage.Sas;
using Microsoft.Extensions.Configuration;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Storage;

/// <summary>
/// Signs <see cref="IBlobUrls"/> as service SAS URLs on one blob of the module's own container, with the account key:
/// an upload URL may only create its blob, a read URL may only read it, and both stop working when their lifetime ends
/// (docs/adr/0002). The container is the connection string <c>blobs</c>, which the AppHost passes from the module's
/// <c>&lt;module&gt;-blobs</c> resource. It is read when the first URL is signed, so a host without it starts and fails
/// only where a URL is needed; signing sends nothing to the storage service.
/// </summary>
public sealed class AzureBlobUrls : IBlobUrls
{
    private readonly TimeProvider time;

    private readonly Lazy<BlobContainerClient> container;

    public AzureBlobUrls(IConfiguration configuration, TimeProvider time)
    {
        Ensure.That(configuration).IsNotNull();
        Ensure.That(time).IsNotNull();
        this.time = time;
        container = new Lazy<BlobContainerClient>(() => ModuleBlobContainer.ForSigning(configuration));
    }

    public Task<BlobUrl> CreateUploadUrlAsync(BlobName name, BlobUrlLifetime lifetime, CancellationToken cancellationToken) =>
        SignAsync(name, lifetime, BlobSasPermissions.Create, cancellationToken);

    public Task<BlobUrl> CreateReadUrlAsync(BlobName name, BlobUrlLifetime lifetime, CancellationToken cancellationToken) =>
        SignAsync(name, lifetime, BlobSasPermissions.Read, cancellationToken);

    private Task<BlobUrl> SignAsync(BlobName name, BlobUrlLifetime lifetime, BlobSasPermissions permissions, CancellationToken cancellationToken)
    {
        Ensure.That(name).IsNotNull();
        Ensure.That(lifetime).IsNotNull();
        cancellationToken.ThrowIfCancellationRequested();

        // Whole seconds, rounded down: the signature carries no fractions, and rounding up would outlive the lifetime.
        var expiresAt = time.GetUtcNow().Add(lifetime.Value);
        expiresAt = new DateTimeOffset(expiresAt.UtcTicks - (expiresAt.UtcTicks % TimeSpan.TicksPerSecond), TimeSpan.Zero);

        var blob = container.Value.GetBlobClient(name.Value);
        var sas = new BlobSasBuilder(permissions, expiresAt)
        {
            BlobContainerName = blob.BlobContainerName,
            BlobName = blob.Name,
            Resource = "b",
        };

        // A signature for an https endpoint works only over https (spr=https), so it never travels in the clear. The
        // emulator, Azurite, serves http, which that would refuse; its URLs name no protocol.
        if (blob.Uri.Scheme == Uri.UriSchemeHttps)
        {
            sas.Protocol = SasProtocol.Https;
        }

        return Task.FromResult(new BlobUrl(blob.GenerateSasUri(sas), expiresAt));
    }
}
