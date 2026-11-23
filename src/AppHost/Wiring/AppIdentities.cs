using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure;

namespace Aspire.Hosting;

/// <summary>
/// The managed identity an app runs under once published, <c>&lt;app&gt;-identity</c>, created the first time something
/// grants it a role and shared by every grant after (docs/adr/0005). Aspire would make one for the roles it assigns by
/// default; those defaults are off for the storage account and the Key Vault, which grant each app only what it uses
/// (<see cref="BlobContainerAccess"/>, <see cref="SecretAccess"/>), so the identity is made here.
/// </summary>
public static class AppIdentities
{
    public static AzureUserAssignedIdentityResource IdentityOf<T>(this IResourceBuilder<T> app)
        where T : IComputeResource
    {
        ArgumentNullException.ThrowIfNull(app);

        if (app.Resource.TryGetLastAnnotation<AppIdentityAnnotation>(out var annotation))
        {
            return annotation.IdentityResource as AzureUserAssignedIdentityResource
                ?? throw new InvalidOperationException($"{app.Resource.Name} runs under an identity that is not a user-assigned one.");
        }

        var identity = app.ApplicationBuilder.AddAzureUserAssignedIdentity($"{app.Resource.Name}-identity");
        app.WithAzureUserAssignedIdentity(identity);
        return identity.Resource;
    }
}
