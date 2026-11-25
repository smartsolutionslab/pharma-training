using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure;
using Azure.Provisioning.KeyVault;

namespace Aspire.Hosting;

/// <summary>
/// Where the AppHost's secrets live once it is published (docs/adr/0005): the Key Vault <c>secrets</c>, in publish mode
/// alone. There every secret is a parameter the deployment sets, with neither a generated value nor a published default,
/// and every Container App reads it as a Key Vault reference, never as a plain secret value of its own. In run mode there
/// is no vault: Aspire's generated passwords stay in the AppHost's user secrets, as they always were. No app gets Aspire's
/// default role on the whole vault; each reads the secrets it references alone (<see cref="SecretAccess"/>).
/// </summary>
public sealed class DeploymentSecrets
{
    private const string VaultName = "secrets";

    private readonly IDistributedApplicationBuilder builder;

    private readonly IResourceBuilder<AzureKeyVaultResource>? vault;

    private readonly Dictionary<string, IResourceBuilder<AzureKeyVaultSecretResource>> stored = [];

    private bool grantsRegistered;

    private DeploymentSecrets(IDistributedApplicationBuilder builder, IResourceBuilder<AzureKeyVaultResource>? vault)
    {
        this.builder = builder;
        this.vault = vault;
    }

    /// <summary>Whether the AppHost is being published, and the vault exists.</summary>
    public bool IsPublishing => vault is not null;

    /// <summary>The Key Vault; publish mode only.</summary>
    public IResourceBuilder<AzureKeyVaultResource> Vault =>
        vault ?? throw new InvalidOperationException($"The Key Vault '{VaultName}' exists in publish mode only.");

    public static DeploymentSecrets For(IDistributedApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return new DeploymentSecrets(builder, builder.ExecutionContext.IsPublishMode ? builder.AddAzureKeyVault(VaultName).ClearDefaultRoleAssignments()
            // Reached through its private endpoint alone (Wiring/PrivateNetwork.cs, #149).
            .ConfigureInfrastructure(infrastructure => PrivateNetwork.DenyPublicAccess(infrastructure.GetProvisionableResources().OfType<KeyVaultService>().Single())) : null);
    }

    /// <summary>
    /// A secret parameter the deployment must set (<c>Parameters__&lt;name&gt;</c>), stored in the vault under its own
    /// name; publish mode only. The vault's secret is a resource of its own, which may not share the parameter's name.
    /// </summary>
    public IResourceBuilder<ParameterResource> Secret(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var parameter = builder.AddParameter(name, secret: true);
        stored.Add(name, Vault.AddSecret($"{name}-secret", name, parameter));
        return parameter;
    }

    /// <summary>
    /// A value made of secrets (a connection string with its password), stored in the vault as one secret, so a Container
    /// App can read it as one Key Vault reference; publish mode only.
    /// </summary>
    public IResourceBuilder<AzureKeyVaultSecretResource> Store(string name, ReferenceExpression value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(value);

        var secret = Vault.AddSecret($"{name}-secret", name, value);
        stored.Add(name, secret);
        return secret;
    }

    /// <summary>The vault's secret that holds a parameter, where <see cref="Secret"/> stored it.</summary>
    public IResourceBuilder<AzureKeyVaultSecretResource>? Holding(IResourceBuilder<ParameterResource> parameter)
    {
        ArgumentNullException.ThrowIfNull(parameter);

        return stored.TryGetValue(parameter.Resource.Name, out var secret) && ReferenceEquals(secret.Resource.Value, parameter.Resource)
            ? secret
            : null;
    }

    /// <summary>Records that each app's grants are registered (<see cref="SecretAccess"/>); false when they already were.</summary>
    internal bool RegisterGrants()
    {
        if (grantsRegistered)
        {
            return false;
        }

        grantsRegistered = true;
        return true;
    }

    /// <summary>The vault's secret <see cref="Store"/> stored under a name; publish mode only.</summary>
    public IResourceBuilder<AzureKeyVaultSecretResource> Stored(string name) =>
        stored.TryGetValue(name, out var secret)
            ? secret
            : throw new InvalidOperationException($"The Key Vault '{VaultName}' holds no secret '{name}'.");
}
