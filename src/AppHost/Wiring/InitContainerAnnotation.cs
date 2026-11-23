using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure;

namespace Aspire.Hosting;

/// <summary>
/// An init container a published app runs before its own (docs/adr/0005): its name, the resource whose image it runs, the
/// identity that resolves its Key Vault references, which no container may use, and its environment, each value a string
/// or an <see cref="IAzureKeyVaultSecretReference"/>. <see cref="Migrations"/> records it and writes it into the app's
/// Bicep; <see cref="SecretAccess"/> grants its identity the secrets it reads.
/// </summary>
public sealed record InitContainerAnnotation(
    string Name,
    IResource Image,
    AzureUserAssignedIdentityResource Identity,
    IReadOnlyDictionary<string, object> Environment) : IResourceAnnotation;
