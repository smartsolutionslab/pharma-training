using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure;
using Azure.Provisioning;
using Azure.Provisioning.Expressions;
using Azure.Provisioning.KeyVault;

namespace Aspire.Hosting;

/// <summary>
/// Published, every app reads the Key Vault secrets it references and no others (docs/adr/0005): its identity is a Key
/// Vault Secrets User on each of those secrets, never on the vault, in one Bicep module per app,
/// <c>&lt;app&gt;-roles-secrets</c>. Which secrets an app references is read from its environment once every resource is
/// added, so a secret given to an app anywhere in the AppHost is granted with it, and nothing is listed by hand. A secret
/// another resource writes into the vault (the Flexible Server its connection strings) is granted once that resource is
/// deployed: the module takes the writer's name, so main.bicep deploys it after the writer.
/// </summary>
public static class SecretAccess
{
    private const string VaultName = "secrets_outputs_name";

    /// <summary>Grants each app the secrets it references; call it once, after the last resource is added.</summary>
    public static async Task GrantEachAppTheSecretsItReadsAsync(this DeploymentSecrets secrets, IDistributedApplicationBuilder builder, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(secrets);
        ArgumentNullException.ThrowIfNull(builder);

        if (!secrets.IsPublishing)
        {
            return;
        }

        var vault = secrets.Vault.Resource;
        foreach (var app in builder.Resources.OfType<IComputeResource>().ToList())
        {
            var names = await SecretsReadByAsync(app, vault, builder.ExecutionContext, cancellationToken);
            if (names.Count == 0)
            {
                continue;
            }

            var identity = builder.CreateResourceBuilder(app).IdentityOf();
            var grants = builder.AddAzureInfrastructure($"{app.Name}-roles-secrets", infrastructure => Grant(infrastructure, app.Name, names))
                .WithParameter(RoleAssignments.PrincipalId, identity.PrincipalId)
                .WithParameter(VaultName, vault.NameOutputReference);
            foreach (var writer in names.Values.OfType<AzureBicepResource>().Distinct())
            {
                grants.WithParameter(WriterName(writer), new BicepOutputReference("name", writer));
            }
        }
    }

    /// <summary>
    /// The vault's secrets an app's environment refers to, directly or inside a connection string, by name, each with the
    /// resource that writes it into the vault where that is not the vault itself.
    /// </summary>
    private static async Task<IReadOnlyDictionary<string, IResource?>> SecretsReadByAsync(IComputeResource app, AzureKeyVaultResource vault, DistributedApplicationExecutionContext executionContext, CancellationToken cancellationToken)
    {
        SortedDictionary<string, IResource?> names = [with(StringComparer.Ordinal)];
        if (app is not IResourceWithEnvironment withEnvironment || !withEnvironment.TryGetEnvironmentVariables(out var callbacks))
        {
            return names;
        }

        Dictionary<string, object> environment = [];
        var context = new EnvironmentCallbackContext(executionContext, withEnvironment, environment, cancellationToken);
        foreach (var callback in callbacks)
        {
            await callback.Callback(context);
        }

        HashSet<object> seen = [with(ReferenceEqualityComparer.Instance)];
        foreach (var value in environment.Values)
        {
            Collect(value, vault, names, seen);
        }

        return names;
    }

    private static void Collect(object? value, AzureKeyVaultResource vault, SortedDictionary<string, IResource?> names, HashSet<object> seen)
    {
        if (value is null || !seen.Add(value))
        {
            return;
        }

        switch (value)
        {
            case IAzureKeyVaultSecretReference secret:
                if (ReferenceEquals(secret.Resource, vault))
                {
                    names[secret.SecretName] = ReferenceEquals(secret.SecretOwner, vault) ? null : secret.SecretOwner;
                }

                break;
            case ConnectionStringReference reference:
                Collect(reference.Resource.ConnectionStringExpression, vault, names, seen);
                break;
            case IResourceWithConnectionString resource:
                Collect(resource.ConnectionStringExpression, vault, names, seen);
                break;
            case ReferenceExpression expression:
                foreach (var provider in expression.ValueProviders)
                {
                    Collect(provider, vault, names, seen);
                }

                break;
        }
    }

    private static void Grant(AzureResourceInfrastructure infrastructure, string app, IReadOnlyDictionary<string, IResource?> names)
    {
        var principalId = new ProvisioningParameter(RoleAssignments.PrincipalId, typeof(string));
        var vaultName = new ProvisioningParameter(VaultName, typeof(string));
        infrastructure.Add(principalId);
        infrastructure.Add(vaultName);

        var vault = KeyVaultService.FromExisting("secrets");
        vault.Name = vaultName;
        infrastructure.Add(vault);

        Dictionary<IResource, ProvisioningParameter> writers = [];
        foreach (var (name, writer) in names)
        {
            var secret = KeyVaultSecret.FromExisting(Infrastructure.NormalizeBicepIdentifier($"secret_{name}"));
            secret.Name = name;
            secret.Parent = vault;
            infrastructure.Add(secret);
            var grant = RoleAssignments.Grant(
                secret,
                secret.Id,
                KeyVaultBuiltInRole.GetBuiltInRoleName(KeyVaultBuiltInRole.KeyVaultSecretsUser),
                KeyVaultBuiltInRole.KeyVaultSecretsUser.ToString(),
                principalId);
            if (writer is AzureBicepResource)
            {
                if (!writers.TryGetValue(writer, out var writerName))
                {
                    writerName = new ProvisioningParameter(WriterName(writer), typeof(string));
                    infrastructure.Add(writerName);
                    writers.Add(writer, writerName);
                }

                // The writer's name, which only its deployment outputs, orders this assignment after the secret exists.
                grant.Description = new InterpolatedStringExpression(
                    [new StringLiteralExpression($"{app} reads {name}, which "), new IdentifierExpression(writerName.BicepIdentifier), new StringLiteralExpression(" writes")]);
            }

            infrastructure.Add(grant);
        }
    }

    private static string WriterName(IResource writer) => Infrastructure.NormalizeBicepIdentifier($"{writer.Name}_outputs_name");
}
