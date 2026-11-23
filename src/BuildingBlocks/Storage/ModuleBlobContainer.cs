using Azure.Storage.Blobs;
using Microsoft.Extensions.Configuration;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Storage;

/// <summary>
/// The module's own container, from the connection string <c>blobs</c> the AppHost passes from the module's
/// <c>&lt;module&gt;-blobs</c> resource (docs/adr/0002): in run mode Azurite's, with the account key; published, the blob
/// service's endpoint and the container without a key, read under the service's managed identity. Every failure names
/// what is missing, so a host without it fails where it first needs the container, saying why.
/// </summary>
internal static class ModuleBlobContainer
{
    public const string ConnectionStringName = "blobs";

    public const string AccountKeyKey = "AccountKey";

    private const string ContainerNameKey = "ContainerName";

    // Aspire publishes an Azure container as Endpoint=<blob service>;ContainerName=<container>; an account connection
    // string names its blob service BlobEndpoint.
    private static readonly string[] EndpointKeys = ["Endpoint", "BlobEndpoint"];

    /// <summary>The container, to read what lies in it, or to write into it, with the client options given.</summary>
    public static BlobContainerClient From(IConfiguration configuration, BlobClientOptions? options = null)
    {
        var location = Locate(configuration);
        var service = location.AccountConnectionString is { } account
            ? new BlobServiceClient(account, options)
            : new BlobServiceClient(location.ServiceUri, ServiceIdentity.Credential(configuration), options);
        return service.GetBlobContainerClient(location.ContainerName);
    }

    /// <summary>The container, to sign URLs for its blobs with the storage account's key.</summary>
    public static BlobContainerClient ForSigning(ModuleBlobLocation location)
    {
        Ensure.That(location).IsNotNull();

        var client = new BlobServiceClient(location.AccountConnectionString ?? throw new InvalidOperationException(
            $"ConnectionStrings:{ConnectionStringName} has no {AccountKeyKey} to sign with; without one, blob URLs are signed with a user delegation key.")).GetBlobContainerClient(location.ContainerName);
        if (!client.CanGenerateSasUri)
        {
            throw new InvalidOperationException(
                $"ConnectionStrings:{ConnectionStringName} cannot sign blob URLs with its {AccountKeyKey}.");
        }

        return client;
    }

    /// <summary>Where the module's container is, and whether the connection string has the account's key.</summary>
    public static ModuleBlobLocation Locate(IConfiguration configuration)
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

        var endpoint = EndpointKeys.Select(key => Value(parts, key)).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        if (!string.IsNullOrWhiteSpace(Value(parts, AccountKeyKey)))
        {
            var account = string.Join(';', parts.Where(part => !IsKey(part, ContainerNameKey)));
            return new ModuleBlobLocation(containerName, account, new BlobServiceClient(account).Uri);
        }

        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var serviceUri))
        {
            throw new InvalidOperationException(
                $"ConnectionStrings:{ConnectionStringName} has neither an {AccountKeyKey} nor the blob service's endpoint (Endpoint=https://<account>.blob.core.windows.net/) to sign and read keyless with.");
        }

        return new ModuleBlobLocation(containerName, AccountConnectionString: null, serviceUri);
    }

    private static string? Value(IEnumerable<string> parts, string key) =>
        parts.Where(part => IsKey(part, key)).Select(part => part[(part.IndexOf('=', StringComparison.Ordinal) + 1)..].Trim()).FirstOrDefault();

    private static bool IsKey(string part, string key) =>
        part.IndexOf('=', StringComparison.Ordinal) is var separator and > 0
            && string.Equals(part[..separator].Trim(), key, StringComparison.OrdinalIgnoreCase);
}
