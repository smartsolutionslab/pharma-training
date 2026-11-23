using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure;
using Azure.Provisioning;
using Azure.Provisioning.AppContainers;
using Azure.Provisioning.Expressions;
using Azure.Provisioning.Resources;

namespace Aspire.Hosting;

/// <summary>
/// Each module's migrations run before its service, in its database alone (decisions B9 and OQ1 on #114, docs/adr/0005).
/// In run mode every service waits for the completion of a MigrationRunner project of its own, <c>&lt;module&gt;-migrations</c>,
/// as it always did. Published, the MigrationRunner is one image, pushed as the Container Apps job <c>migrations</c>, which
/// has a manual trigger, no secrets and no identity of its own, and which nobody starts: it runs as each service's init
/// container <c>migrations</c> instead, on every new replica, before the service's own container. The init container reads
/// the module's administrator connection string as a Key Vault reference resolved by a second identity of the app's,
/// <c>&lt;module&gt;-migrations-identity</c>, whose lifecycle is <c>None</c>: no container, init or main, may get a token for
/// it, and the service's own identity (lifecycle <c>Main</c>) is never granted that secret for it. It reads the connection
/// string the service logs in with too, the module role's (<see cref="Databases.RoleConnectionString"/>), as
/// <c>ConnectionStrings__application</c>, and creates or updates that role after the migrations. Nothing here is listed
/// per module: the AppHost's loop over the services calls <see cref="RunsMigrationsFirst"/> for each.
/// </summary>
public static class Migrations
{
    private const string Name = "migrations";

    private const string ModuleVariable = "Migrations__Module";

    private const string AdministratorVariable = "ConnectionStrings__database";

    // The connection string the service logs in with, whose role the runner creates and grants.
    private const string ApplicationVariable = "ConnectionStrings__application";

    /// <summary>Published, the <c>migrations</c> job that carries the MigrationRunner's image; nothing in run mode.</summary>
    public static IResourceBuilder<ProjectResource>? AddMigrations(this IDistributedApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (!builder.ExecutionContext.IsPublishMode)
        {
            return null;
        }

        // No reference and no environment of its own: no secret, no Key Vault reference and no identity besides the
        // registry's, which pulls the image. Its only purpose is that the image is built and pushed.
        return builder.AddProject<Projects.MigrationRunner>(Name)
            .PublishAsAzureContainerAppJob((_, job) =>
            {
                job.Configuration.TriggerType = ContainerAppJobTriggerType.Manual;
                job.Configuration.ManualTriggerConfig = new JobConfigurationManualTriggerConfig { Parallelism = 1, ReplicaCompletionCount = 1 };
                job.Configuration.ReplicaRetryLimit = 0;
                job.Configuration.ReplicaTimeout = 1800;
            });
    }

    /// <summary>
    /// Runs <paramref name="module"/>'s migrations in <paramref name="database"/> before <paramref name="service"/>: in run
    /// mode as a project the service waits for, published as the service's init container running
    /// <paramref name="migrations"/>' image.
    /// </summary>
    public static IResourceBuilder<ProjectResource> RunsMigrationsFirst(
        this IResourceBuilder<ProjectResource> service,
        string module,
        IResourceBuilder<IResourceWithConnectionString> database,
        IResourceBuilder<ProjectResource>? migrations,
        DeploymentSecrets secrets)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentException.ThrowIfNullOrWhiteSpace(module);
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(secrets);

        if (!service.ApplicationBuilder.ExecutionContext.IsPublishMode)
        {
            var runner = service.ApplicationBuilder.AddProject<Projects.MigrationRunner>($"{module}-migrations")
                .WithConnectionStringsOnly(secrets)
                .WithReference(database, "database")
                .WithEnvironment(ModuleVariable, module)
                .WaitFor(database);
            return service.WaitForCompletion(runner);
        }

        if (migrations is null)
        {
            throw new InvalidOperationException($"Published, {service.Resource.Name}'s migrations run as its init container from the migrations job's image; pass the job AddMigrations returned.");
        }

        var own = service.IdentityOf();
        var init = new InitContainerAnnotation(
            Name,
            migrations.Resource,
            service.AdditionalIdentity(Name),
            new Dictionary<string, object>(StringComparer.Ordinal)
            {
                [ModuleVariable] = module,
                [AdministratorVariable] = AdministratorSecret(database.Resource),
                [ApplicationVariable] = Databases.RoleConnectionString(database, secrets),
            });
        // Container Apps ignores waits; the publish pipeline orders the service's deployment after the job's image is pushed.
        return service.WithAnnotation(init)
            .WaitFor(migrations)
            .PublishAsAzureContainerApp((infrastructure, app) => AddInitContainer(infrastructure, app, init, own));
    }

    /// <summary>
    /// The Key Vault secret that holds the database's connection string as the Flexible Server writes it, with the
    /// administrator's login, taken from the connection string itself rather than spelled after Aspire's naming.
    /// </summary>
    private static IAzureKeyVaultSecretReference AdministratorSecret(IResourceWithConnectionString database)
    {
        List<IAzureKeyVaultSecretReference> secrets = [];
        Collect(database.ConnectionStringExpression, secrets, [with(ReferenceEqualityComparer.Instance)]);
        return secrets.Distinct().ToList() is [var secret]
            ? secret
            : throw new InvalidOperationException($"{database.Name}'s connection string should be one Key Vault secret once published; it holds {secrets.Count}.");
    }

    private static void Collect(object? value, List<IAzureKeyVaultSecretReference> secrets, HashSet<object> seen)
    {
        if (value is null || !seen.Add(value))
        {
            return;
        }

        switch (value)
        {
            case IAzureKeyVaultSecretReference secret:
                secrets.Add(secret);
                break;
            case ConnectionStringReference reference:
                Collect(reference.Resource.ConnectionStringExpression, secrets, seen);
                break;
            case IResourceWithConnectionString resource:
                Collect(resource.ConnectionStringExpression, secrets, seen);
                break;
            case ReferenceExpression expression:
                foreach (var provider in expression.ValueProviders)
                {
                    Collect(provider, secrets, seen);
                }

                break;
        }
    }

    /// <summary>
    /// Appends the init container to the app Aspire built: its image, its environment, a secret per Key Vault reference
    /// resolved by the init container's identity, and the identities' lifecycles. Its environment never reaches the main
    /// container, which <c>WithEnvironment</c> would give it.
    /// </summary>
    private static void AddInitContainer(AzureResourceInfrastructure infrastructure, ContainerApp app, InitContainerAnnotation init, AzureUserAssignedIdentityResource own)
    {
        var image = new ContainerImageReference(init.Image).AsProvisioningParameter(infrastructure, $"{Infrastructure.NormalizeBicepIdentifier(init.Image.Name)}_containerimage");
        BicepValue<string> identity = init.Identity.Id.AsProvisioningParameter(infrastructure);
        BicepValue<string> ownIdentity = own.Id.AsProvisioningParameter(infrastructure);

        var container = new ContainerAppInitContainer { Name = init.Name, Image = image };
        foreach (var (name, value) in init.Environment)
        {
            switch (value)
            {
                case string text:
                    container.Env.Add(new ContainerAppEnvironmentVariable { Name = name, Value = text });
                    break;
                case IAzureKeyVaultSecretReference secret:
                    var secretName = $"{init.Name}-{name.Replace('_', '-').ToLowerInvariant()}";
                    app.Configuration.Secrets.Add(new ContainerAppWritableSecret
                    {
                        Name = secretName,
                        KeyVaultUri = secret.AsKeyVaultSecret(infrastructure).Properties.SecretUri,
                        Identity = identity,
                    });
                    container.Env.Add(new ContainerAppEnvironmentVariable { Name = name, SecretRef = secretName });
                    break;
                default:
                    throw new InvalidOperationException($"{app.BicepIdentifier}'s init container {init.Name} is given {name} as a {value.GetType().Name}; only text and Key Vault secrets are written.");
            }
        }

        app.Template.InitContainers.Add(container);
        app.Identity.UserAssignedIdentities[BicepFunction.Interpolate($"{identity}").Compile().ToString()] = new UserAssignedIdentityDetails();
        // The platform resolves the init container's Key Vault references with its identity; no container may get a token
        // for it. The service's own identity serves its main container alone.
        app.Configuration.IdentitySettings.Add(new ContainerAppIdentitySettings { Identity = identity, Lifecycle = ContainerAppIdentitySettingsLifeCycle.None });
        app.Configuration.IdentitySettings.Add(new ContainerAppIdentitySettings { Identity = ownIdentity, Lifecycle = ContainerAppIdentitySettingsLifeCycle.Main });
    }
}
