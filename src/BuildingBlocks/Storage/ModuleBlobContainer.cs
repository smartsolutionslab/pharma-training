using Azure.Storage.Blobs;
using Microsoft.Extensions.Configuration;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Storage;

/// <summary>
/// The module's own container, from the connection string <c>blobs</c> the AppHost passes from the module's
/// <c>&lt;module&gt;-blobs</c> resource (docs/adr/0002). Every failure names what is missing, so a host without it fails
/// where it first needs the container, saying why.
/// </summary>
internal static class ModuleBlobContainer
{
    private const string ConnectionStringName = "blobs";

    private const string ContainerNameKey = "ContainerName";

    private const string AccountKeyKey = "AccountKey";

    /// <summary>The container, to read what lies in it.</summary>
    public static BlobContainerClient From(IConfiguration configuration) => Create(configuration, signs: false);

    /// <summary>The container, to sign URLs for its blobs with the storage account's key.</summary>
    public static BlobContainerClient ForSigning(IConfiguration configuration) => Create(configuration, signs: true);

    private static BlobContainerClient Create(IConfiguration configuration, bool signs)
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

        if (signs && string.IsNullOrWhiteSpace(Value(parts, AccountKeyKey)))
        {
            throw new InvalidOperationException(
                $"ConnectionStrings:{ConnectionStringName} has no {AccountKeyKey}: blob URLs are signed with the storage account's key. Keyless signing (a user delegation key, with managed identity) is a separate decision (docs/adr/0002).");
        }

        var account = string.Join(';', parts.Where(part => !IsKey(part, ContainerNameKey)));
        var client = new BlobServiceClient(account).GetBlobContainerClient(containerName);
        if (signs && !client.CanGenerateSasUri)
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
