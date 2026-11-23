using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure;

namespace Aspire.Hosting;

/// <summary>
/// The managed identity an app runs under once published, <c>&lt;app&gt;-identity</c>, created the first time something
/// grants it a role and shared by every grant after (docs/adr/0005). Aspire would make one for the roles it assigns by
/// default; those defaults are off for the storage account and the Key Vault, which grant each app only what it uses
/// (<see cref="BlobContainerAccess"/>, <see cref="SecretAccess"/>), so the identity is made here. Publish mode only, where
/// the Key Vault has registered Azure provisioning already: the identity is added as a resource alone, so it can be made
/// when publishing starts too, once the model is built (<see cref="SecretAccess"/>).
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

        var identity = app.ApplicationBuilder.AddResource(new AzureUserAssignedIdentityResource($"{app.Resource.Name}-identity"));
        app.WithAzureUserAssignedIdentity(identity);
        return identity.Resource;
    }

    /// <summary>
    /// A further identity of an app's, <c>&lt;app&gt;-&lt;purpose&gt;-identity</c>, which is not the one it runs under: the
    /// app's Bicep names it where it is needed (an init container's Key Vault references, <see cref="Migrations"/>).
    /// </summary>
    public static AzureUserAssignedIdentityResource AdditionalIdentity<T>(this IResourceBuilder<T> app, string purpose)
        where T : IComputeResource
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);

        var name = $"{app.Resource.Name}-{purpose}-identity";
        return app.ApplicationBuilder.Resources.OfType<AzureUserAssignedIdentityResource>().SingleOrDefault(identity => identity.Name == name)
            ?? app.ApplicationBuilder.AddResource(new AzureUserAssignedIdentityResource(name)).Resource;
    }
}
