using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure;

namespace Aspire.Hosting;

/// <summary>
/// Gives a resource its secrets as environment variables: in run mode the parameters themselves, once published the Key
/// Vault secrets that hold them (<see cref="DeploymentSecrets"/>), which a Container App reads as Key Vault references.
/// Published, a secret parameter the vault does not hold is a mistake in the AppHost, never a plain value: it fails.
/// </summary>
public static class SecretEnvironment
{
    public static IResourceBuilder<T> WithSecret<T>(this IResourceBuilder<T> resource, string name, IResourceBuilder<ParameterResource> parameter, DeploymentSecrets secrets)
        where T : IResourceWithEnvironment
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(parameter);
        ArgumentNullException.ThrowIfNull(secrets);

        if (secrets.Holding(parameter) is { } secret)
        {
            IAzureKeyVaultSecretReference reference = secret.Resource;
            return resource.WithEnvironment(name, reference);
        }

        if (secrets.IsPublishing && parameter.Resource.Secret)
        {
            throw new InvalidOperationException(
                $"{resource.Resource.Name} is given the secret parameter '{parameter.Resource.Name}' as {name}, but the Key Vault does not hold it; published, a secret reaches an app only through the vault. Create the parameter with DeploymentSecrets.Secret(\"{parameter.Resource.Name}\").");
        }

        return resource.WithEnvironment(name, parameter);
    }

    /// <summary>
    /// Published, a resource reads what it references as connection strings, service discovery and endpoints, not as
    /// connection properties too: Aspire would also hand it each part of a connection (<c>DATABASE_PASSWORD</c>,
    /// <c>DATABASE_URI</c> with the password in it) as a plain secret value, which nothing here reads. Run mode keeps
    /// Aspire's default.
    /// </summary>
    public static IResourceBuilder<T> WithConnectionStringsOnly<T>(this IResourceBuilder<T> resource, DeploymentSecrets secrets)
        where T : IResourceWithEnvironment
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(secrets);

        return secrets.IsPublishing
            ? resource.WithReferenceEnvironment(ReferenceEnvironmentInjectionFlags.ConnectionString | ReferenceEnvironmentInjectionFlags.ServiceDiscovery | ReferenceEnvironmentInjectionFlags.Endpoints)
            : resource;
    }
}
