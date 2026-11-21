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
    private const string ConnectionStringName = "blobs";

    private const string ContainerNameKey = "ContainerName";

    private const string AccountKeyKey = "AccountKey";

    private readonly TimeProvider time;

    private readonly Lazy<BlobContainerClient> container;

    public AzureBlobUrls(IConfiguration configuration, TimeProvider time)
    {
        Ensure.That(configuration).IsNotNull();
        Ensure.That(time).IsNotNull();
        this.time = time;
        container = new Lazy<BlobContainerClient>(() => Container(configuration));
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
        return Task.FromResult(new BlobUrl(blob.GenerateSasUri(sas), expiresAt));
    }

    private static BlobContainerClient Container(IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"No blob container is configured: ConnectionStrings:{ConnectionStringName} is missing. The AppHost passes it to every service from its module's '<module>-blobs' resource.");
        }

        // Kept as written, not through DbConnectionStringBuilder, which quotes a base64 key's trailing '=' the SDK then reads.
        var parts = connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var containerName = Value(parts, ContainerNameKey);
        if (string.IsNullOrWhiteSpace(containerName))
        {
            throw new InvalidOperationException(
                $"ConnectionStrings:{ConnectionStringName} names no container ({ContainerNameKey}=...); it must be the module's own '<module>-blobs' resource from the AppHost, not the storage account's.");
        }

        if (string.IsNullOrWhiteSpace(Value(parts, AccountKeyKey)))
        {
            throw new InvalidOperationException(
                $"ConnectionStrings:{ConnectionStringName} has no {AccountKeyKey}: blob URLs are signed with the storage account's key. Keyless signing (a user delegation key, with managed identity) is a separate decision (docs/adr/0002).");
        }

        var account = string.Join(';', parts.Where(part => !IsKey(part, ContainerNameKey)));
        var client = new BlobServiceClient(account).GetBlobContainerClient(containerName);
        if (!client.CanGenerateSasUri)
        {
            throw new InvalidOperationException(
                $"ConnectionStrings:{ConnectionStringName} cannot sign blob URLs: they are signed with the storage account's key. Keyless signing is a separate decision (docs/adr/0002).");
        }

        return client;
    }

    private static string? Value(IEnumerable<string> parts, string key) =>
        parts.Where(part => IsKey(part, key)).Select(part => part[(part.IndexOf('=', StringComparison.Ordinal) + 1)..].Trim()).FirstOrDefault();

    private static bool IsKey(string part, string key) =>
        part.IndexOf('=', StringComparison.Ordinal) is var separator and > 0
            && string.Equals(part[..separator].Trim(), key, StringComparison.OrdinalIgnoreCase);
}
