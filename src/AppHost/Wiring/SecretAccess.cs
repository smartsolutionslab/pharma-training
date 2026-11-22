using System.Globalization;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure;
using Azure.Provisioning;
using Azure.Provisioning.Expressions;
using Azure.Provisioning.KeyVault;

namespace Aspire.Hosting;

/// <summary>
/// Published, every app reads the Key Vault secrets it references and no others (docs/adr/0005): its identity is a Key
/// Vault Secrets User on each of those secrets, never on the vault, in one Bicep module per app,
/// <c>&lt;app&gt;-roles-secrets</c>. Which secrets an app references is read from its environment and its arguments when
/// the AppHost starts publishing, once the model is final, so a secret or an app added anywhere in the AppHost, before or
/// after the grants are registered, is granted, and nothing is listed by hand. A secret another resource writes into the
/// vault (the Flexible Server its connection strings) is granted once that resource is deployed: the module takes the
/// writer's name, so main.bicep deploys it after the writer. Each app is deployed on its own and takes the module's output
/// <c>grants</c> as a parameter, so it starts only once its identity may read what it references.
/// </summary>
public static class SecretAccess
{
    private const string VaultName = "secrets_outputs_name";

    // The output of an app's <app>-roles-secrets module the app takes, so its deployment waits for the grants.
    private const string GrantsOutput = "grants";

    // The tag the app carries it under: how many secrets its identity was granted.
    private const string GrantsTag = "key-vault-secrets-granted";

    /// <summary>
    /// Grants each app the secrets it references, computed when publishing starts from the final model; call it once,
    /// anywhere after <see cref="DeploymentSecrets.For"/>. A second call adds nothing.
    /// </summary>
    public static Task GrantEachAppTheSecretsItReadsAsync(this DeploymentSecrets secrets, IDistributedApplicationBuilder builder, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(secrets);
        ArgumentNullException.ThrowIfNull(builder);
        cancellationToken.ThrowIfCancellationRequested();

        if (!secrets.IsPublishing || !secrets.RegisterGrants())
        {
            return Task.CompletedTask;
        }

        // Subscribed on the builder, so it runs before the Container Apps environment turns each app into its Bicep.
        builder.Eventing.Subscribe<BeforeStartEvent>((@event, token) => GrantAsync(secrets, builder, token));
        return Task.CompletedTask;
    }

    private static async Task GrantAsync(DeploymentSecrets secrets, IDistributedApplicationBuilder builder, CancellationToken cancellationToken)
    {
        var vault = secrets.Vault.Resource;
        foreach (var app in builder.Resources.OfType<IComputeResource>().ToList())
        {
            var names = await SecretsReadByAsync(app, vault, builder.ExecutionContext, cancellationToken);
            if (names.Count == 0)
            {
                continue;
            }

            var identity = builder.CreateResourceBuilder(app).IdentityOf();
            // Added as a resource alone: AddAzureInfrastructure would register Azure provisioning again, which the Key Vault
            // has done, and the services cannot change once the model is built.
            var grants = builder.AddResource(new AzureProvisioningResource($"{app.Name}-roles-secrets", infrastructure => Grant(infrastructure, app.Name, names)))
                .WithParameter(RoleAssignments.PrincipalId, identity.PrincipalId)
                .WithParameter(VaultName, vault.NameOutputReference);
            foreach (var writer in names.Values.OfType<AzureBicepResource>().Distinct())
            {
                grants.WithParameter(WriterName(writer), new BicepOutputReference("name", writer));
            }

            var granted = new BicepOutputReference(GrantsOutput, grants.Resource);
            // As PublishAsAzureContainerApp would, for a project, a container or an executable alike.
            app.Annotations.Add(new AzureContainerAppCustomizationAnnotation((infrastructure, containerApp) =>
                containerApp.Tags[GrantsTag] = granted.AsProvisioningParameter(infrastructure)));
        }
    }

    /// <summary>
    /// The vault's secrets an app's environment or arguments refer to, directly or inside a connection string, by name, each
    /// with the resource that writes it into the vault where that is not the vault itself.
    /// </summary>
    private static async Task<IReadOnlyDictionary<string, IResource?>> SecretsReadByAsync(IComputeResource app, AzureKeyVaultResource vault, DistributedApplicationExecutionContext executionContext, CancellationToken cancellationToken)
    {
        SortedDictionary<string, IResource?> names = [with(StringComparer.Ordinal)];
        List<object> values = [];
        if (app is IResourceWithEnvironment withEnvironment && withEnvironment.TryGetEnvironmentVariables(out var environmentCallbacks))
        {
            Dictionary<string, object> environment = [];
            var context = new EnvironmentCallbackContext(executionContext, withEnvironment, environment, cancellationToken);
            foreach (var callback in environmentCallbacks)
            {
                await callback.Callback(context);
            }

            values.AddRange(environment.Values);
        }

        if (app.TryGetAnnotationsOfType<CommandLineArgsCallbackAnnotation>(out var argumentCallbacks))
        {
            List<object> arguments = [];
            var context = new CommandLineArgsCallbackContext(arguments, app, cancellationToken) { ExecutionContext = executionContext };
            foreach (var callback in argumentCallbacks)
            {
                await callback.Callback(context);
            }

            values.AddRange(arguments);
        }

        HashSet<object> seen = [with(ReferenceEqualityComparer.Instance)];
        foreach (var value in values)
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

        // Read by the app's own deployment, which therefore waits for this module.
        infrastructure.Add(new ProvisioningOutput(GrantsOutput, typeof(string)) { Value = names.Count.ToString(CultureInfo.InvariantCulture) });
    }

    private static string WriterName(IResource writer) => Infrastructure.NormalizeBicepIdentifier($"{writer.Name}_outputs_name");
}
