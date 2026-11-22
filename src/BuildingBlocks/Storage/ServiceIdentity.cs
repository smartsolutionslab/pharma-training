using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Configuration;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Storage;

/// <summary>
/// The service's own managed identity, which a published service reads and signs for its container under: the
/// user-assigned identity whose client id the AppHost passes as <c>AZURE_CLIENT_ID</c> (docs/adr/0002). Nothing else is
/// tried: no developer sign-in, no environment credentials.
/// </summary>
internal static class ServiceIdentity
{
    private const string ClientIdKey = "AZURE_CLIENT_ID";

    public static TokenCredential Credential(IConfiguration configuration)
    {
        var clientId = configuration[ClientIdKey];
        if (string.IsNullOrWhiteSpace(clientId))
        {
            throw new InvalidOperationException(
                $"ConnectionStrings:{ModuleBlobContainer.ConnectionStringName} has no {ModuleBlobContainer.AccountKeyKey}, and {ClientIdKey} names no managed identity to sign and read keyless with. Published, the AppHost gives every service the client id of its own identity; in run mode the connection string has Azurite's account key (docs/adr/0002).");
        }

        return new ManagedIdentityCredential(ManagedIdentityId.FromUserAssignedClientId(clientId));
    }
}
