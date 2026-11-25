using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Dotnet;

namespace Aspire.Hosting;

/// <summary>
/// The confidential client <c>pharma-training-erasure</c>, with which a service deletes an erased person's Keycloak account
/// (#115 slice 6, docs/adr/0041). Its secret is one secret parameter, <c>keycloak-erasure-client-secret</c>: in run mode
/// Aspire generates it once and keeps it in the AppHost's user secrets, letters and digits alone, so it needs no escaping
/// in the realm's JSON or a form body; published, it is a Key Vault secret the deployment sets (docs/adr/0005). Keycloak's
/// realm import reads it as <c>KEYCLOAK_ERASURE_CLIENT_SECRET</c>, and a service whose project says
/// <c>&lt;ManagesAccounts&gt;true&lt;/ManagesAccounts&gt;</c> as <c>Keycloak__Accounts__ClientSecret</c>, beside Keycloak's
/// internal address, the realm and the client's id. No other app reads it: it administers every account.
/// </summary>
public static class AccountDeletion
{
    /// <summary>The client's id in both realms.</summary>
    public const string ClientId = "pharma-training-erasure";

    private const string SecretName = "keycloak-erasure-client-secret";

    /// <summary>Gives Keycloak's realm import the client's secret, and returns its parameter for the services that manage accounts.</summary>
    public static IResourceBuilder<ParameterResource> WithErasureClient(this IResourceBuilder<ContainerResource> keycloak, DeploymentSecrets secrets)
    {
        ArgumentNullException.ThrowIfNull(keycloak);
        ArgumentNullException.ThrowIfNull(secrets);

        var secret = secrets.IsPublishing
            ? secrets.Secret(SecretName)
            : keycloak.ApplicationBuilder.AddParameter(SecretName, new GenerateParameterDefault { MinLength = 32, Special = false }, secret: true, persist: true);
        keycloak.WithSecret("KEYCLOAK_ERASURE_CLIENT_SECRET", secret, secrets);
        return secret;
    }

    /// <summary>
    /// Gives a service that deletes accounts Keycloak's internal address (published, Keycloak's internal URL: the auth
    /// proxy forwards no <c>/admin</c>), the realm, the client's id and its secret, under <c>Keycloak__Accounts__*</c>.
    /// </summary>
    public static IResourceBuilder<DotnetProjectResource> ManagesAccounts(
        this IResourceBuilder<DotnetProjectResource> service,
        IResourceBuilder<ContainerResource> keycloak,
        IResourceBuilder<ParameterResource> secret,
        DeploymentSecrets secrets)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(keycloak);

        return service
            .WithEnvironment("Keycloak__Accounts__Address", ReferenceExpression.Create($"{keycloak.GetEndpoint("http")}"))
            .WithEnvironment("Keycloak__Accounts__Realm", AuthProxy.Realm)
            .WithEnvironment("Keycloak__Accounts__ClientId", ClientId)
            .WithSecret("Keycloak__Accounts__ClientSecret", secret, secrets);
    }
}
