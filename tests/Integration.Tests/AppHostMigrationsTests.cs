using System.Text.RegularExpressions;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// How each module's migrations run before its service (decisions B9 and OQ1 on #114, docs/adr/0005). In run mode every
/// service waits for a MigrationRunner project of its own, <c>&lt;module&gt;-migrations</c>. Published, the runner is one
/// image, pushed as the <c>migrations</c> Container Apps job, which nobody starts: it runs as each service's init
/// container, with the module's administrator connection string resolved by a second identity that no container can use,
/// so the service's own code never holds it.
/// </summary>
public class AppHostMigrationsTests
{
    private const string Migrations = "migrations";

    private const string ApplicationVariable = "ConnectionStrings__application";

    private const string RegistryIdentity = "aca_outputs_azure_container_registry_managed_identity_id";

    private static readonly TimeSpan ModelTimeout = TimeSpan.FromMinutes(1);

    [Fact]
    public async Task Run_mode_keeps_a_migrations_project_per_service()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(ModelTimeout);
        await using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.AppHost>(timeout.Token);
        var services = PublishedAppHost.Services(builder);

        services.ShouldNotBeEmpty();
        builder.Resources.Select(resource => resource.Name).ShouldNotContain(Migrations, "Run mode has no migrations job.");
        foreach (var service in services)
        {
            var module = service.Name;
            var runner = builder.Resources.OfType<ProjectResource>().SingleOrDefault(project => project.Name == $"{module}-migrations");
            runner.ShouldNotBeNull($"{module} has no {module}-migrations project in run mode.");
            Path.GetFileName(runner.GetProjectMetadata().ProjectPath).ShouldBe("MigrationRunner.csproj");

            service.Annotations.OfType<WaitAnnotation>()
                .Where(wait => wait.Resource == runner)
                .Select(wait => wait.WaitType)
                .ShouldBe([WaitType.WaitForCompletion], $"{module} should start only once {module}-migrations has completed.");

            var environment = await EnvironmentAsync(builder, runner);
            environment.ShouldContainKey("Migrations__Module");
            environment["Migrations__Module"].ShouldBe(module);
            environment.ShouldContainKey("ConnectionStrings__database");
            ConnectionStringOf(environment["ConnectionStrings__database"]).ShouldBe($"{module}-database");
        }
    }

    [Fact]
    public async Task No_migrations_run_as_a_container_app_of_their_own()
    {
        await using var builder = await PublishedAppHost.ModelAsync(PublishedAppHost.Staging, TestContext.Current.CancellationToken);
        var modules = PublishedAppHost.Services(builder).Select(service => service.Name).ToList();

        var bicep = await PublishedAppHost.PublishAsync(PublishedAppHost.Staging, TestContext.Current.CancellationToken);

        modules.ShouldNotBeEmpty();
        var perModule = modules.Select(module => $"{module}-migrations").ToList();
        builder.Resources.Select(resource => resource.Name).Intersect(perModule)
            .ShouldBeEmpty("Published, a module's migrations run in its service's init container, not as a resource of their own.");
        bicep.ContainerApps.Keys.Where(app => app == Migrations || app.EndsWith("-migrations", StringComparison.Ordinal))
            .ShouldBeEmpty("These migrations are published as Container Apps of their own.");
        bicep.Files.Keys.Where(path => perModule.Any(name => path == $"{name}/{name}.bicep"))
            .ShouldBeEmpty("Publishing wrote a module for a migrations app of a module's own.");
    }

    [Fact]
    public async Task The_migrations_image_is_published_once_as_a_job_nobody_starts()
    {
        await using var builder = await PublishedAppHost.ModelAsync(PublishedAppHost.Staging, TestContext.Current.CancellationToken);
        var runners = builder.Resources.OfType<ProjectResource>()
            .Where(project => Path.GetFileName(project.GetProjectMetadata().ProjectPath) == "MigrationRunner.csproj")
            .Select(project => project.Name)
            .ToList();

        var bicep = await PublishedAppHost.PublishAsync(PublishedAppHost.Staging, TestContext.Current.CancellationToken);

        runners.ShouldBe([Migrations], "Published, the MigrationRunner should be one resource, migrations.");
        bicep.Files.Where(file => file.Value.Contains("'Microsoft.App/jobs@", StringComparison.Ordinal)).Select(file => file.Key)
            .ShouldBe([$"{Migrations}/{Migrations}.bicep"], "Exactly one Container Apps job should be published, migrations.");
        var job = bicep.Module(Migrations);
        var configuration = PublishedBicep.Block(job, "configuration");
        configuration.ShouldMatch(@"\btriggerType:\s*'Manual'", "The migrations job should start only when someone starts it, and nobody does.");
        PublishedBicep.Block(configuration, "manualTriggerConfig").ShouldMatch(@"\bparallelism:\s*1\b");
        PublishedBicep.Block(configuration, "manualTriggerConfig").ShouldMatch(@"\breplicaCompletionCount:\s*1\b");
        configuration.ShouldMatch(@"\breplicaRetryLimit:\s*0\b");
        PublishedBicep.SecureParameters(job).ShouldBeEmpty("The migrations job receives a secret as a value.");
        PublishedBicep.Array(configuration, "secrets").ShouldBeEmpty("The migrations job has secrets of its own.");
        job.ShouldNotContain("keyVaultUrl", Case.Sensitive, "The migrations job reads a Key Vault secret.");
        UserAssignedIdentities(job).ShouldAllBe(identity => identity == RegistryIdentity, "The migrations job runs under an identity besides the registry's.");
        bicep.Identities().ShouldNotContain($"{Migrations}-identity", "The migrations job has an identity of its own.");
        bicep.Files["main.bicep"].ShouldNotMatch(@"(?m)^module\s+migrations_roles_\w+\s", "The migrations job is granted roles of its own.");
    }

    [Fact]
    public async Task Every_service_runs_its_migrations_first_as_an_init_container()
    {
        await using var builder = await PublishedAppHost.ModelAsync(PublishedAppHost.Staging, TestContext.Current.CancellationToken);
        var modules = PublishedAppHost.Services(builder).Select(service => service.Name).ToList();

        var bicep = await PublishedAppHost.PublishAsync(PublishedAppHost.Staging, TestContext.Current.CancellationToken);

        modules.ShouldNotBeEmpty();
        List<string> problems = [];
        foreach (var module in modules)
        {
            bicep.ContainerApps.ShouldContainKey(module);
            var app = bicep.ContainerApps[module];
            var initContainers = PublishedBicep.Objects(PublishedBicep.Array(app, "initContainers"));
            if (initContainers.Count != 1)
            {
                problems.Add($"{module} has {initContainers.Count} init containers, not one.");
                continue;
            }

            var init = initContainers[0];
            var secrets = PublishedBicep.SecretEntries(app);
            var environment = PublishedBicep.EnvironmentOf(init);
            if (PublishedBicep.Property(init, "name") != Migrations)
            {
                problems.Add($"{module}'s init container is named '{PublishedBicep.Property(init, "name")}', not '{Migrations}'.");
            }

            if (PublishedBicep.Property(init, "image") != $"{Migrations}_containerimage")
            {
                problems.Add($"{module}'s init container runs '{PublishedBicep.Property(init, "image")}', not the migrations job's image ({Migrations}_containerimage).");
            }

            if (environment.SingleOrDefault(entry => entry.Name == "Migrations__Module") is not { Value: var named } || named != module)
            {
                problems.Add($"{module}'s init container does not name its module in Migrations__Module.");
            }

            var administrator = environment.SingleOrDefault(entry => entry.Name == "ConnectionStrings__database");
            var secret = secrets.SingleOrDefault(entry => entry.Name == administrator?.SecretRef);
            if (administrator is null || administrator.SecretRef.Length == 0 || secret is null)
            {
                problems.Add($"{module}'s init container does not read ConnectionStrings__database from a secret of the app.");
            }
            else if (secret.VaultSecret != $"connectionstrings--{module}-database" || secret.Identity != $"{module}-migrations-identity")
            {
                problems.Add($"{module}'s init container reads ConnectionStrings__database from '{secret.VaultSecret}' under '{secret.Identity}', not from 'connectionstrings--{module}-database' under '{module}-migrations-identity'.");
            }

            // The module's role, which the runner creates and grants: the same secret the service's main container logs in with.
            var application = environment.SingleOrDefault(entry => entry.Name == ApplicationVariable);
            var applicationSecret = secrets.SingleOrDefault(entry => entry.Name == application?.SecretRef);
            if (application is null || application.SecretRef.Length == 0 || applicationSecret is null)
            {
                problems.Add($"{module}'s init container does not read {ApplicationVariable} from a secret of the app.");
            }
            else if (applicationSecret.VaultSecret != RoleSecret(module) || applicationSecret.Identity != $"{module}-migrations-identity")
            {
                problems.Add($"{module}'s init container reads {ApplicationVariable} from '{applicationSecret.VaultSecret}' under '{applicationSecret.Identity}', not from '{RoleSecret(module)}' under '{module}-migrations-identity'.");
            }
        }

        problems.ShouldBeEmpty();
    }

    [Fact]
    public async Task Only_the_init_stage_sees_the_administrator()
    {
        await using var builder = await PublishedAppHost.ModelAsync(PublishedAppHost.Staging, TestContext.Current.CancellationToken);
        var modules = PublishedAppHost.Services(builder).Select(service => service.Name).ToList();

        var bicep = await PublishedAppHost.PublishAsync(PublishedAppHost.Staging, TestContext.Current.CancellationToken);

        modules.ShouldNotBeEmpty();
        List<string> problems = [];
        foreach (var module in modules)
        {
            bicep.ContainerApps.ShouldContainKey(module);
            var app = bicep.ContainerApps[module];
            var settings = PublishedBicep.Objects(PublishedBicep.Array(PublishedBicep.Block(app, "configuration"), "identitySettings"))
                .ToDictionary(setting => PublishedBicep.Property(setting, "identity"), setting => PublishedBicep.Property(setting, "lifecycle"), StringComparer.Ordinal);
            var migrationsIdentity = $"{module}_migrations_identity_outputs_id";
            var ownIdentity = $"{module}_identity_outputs_id";
            if (settings.GetValueOrDefault(migrationsIdentity) != "None")
            {
                problems.Add($"{module}-migrations-identity has lifecycle '{settings.GetValueOrDefault(migrationsIdentity)}', not 'None': a container could get a token for it.");
            }

            if (settings.GetValueOrDefault(ownIdentity) != "Main")
            {
                problems.Add($"{module}-identity has lifecycle '{settings.GetValueOrDefault(ownIdentity)}', not 'Main'.");
            }

            var secrets = PublishedBicep.SecretEntries(app);
            var main = PublishedBicep.Objects(PublishedBicep.Array(app, "containers")).SingleOrDefault(container => PublishedBicep.Property(container, "name") == module);
            if (main is null)
            {
                problems.Add($"{module} has no main container named '{module}'.");
                continue;
            }

            var mainReadsTheInitStagesSecrets = PublishedBicep.EnvironmentOf(main)
                .Where(entry => entry.SecretRef.Length > 0)
                .Where(entry => secrets.Any(secret => secret.Name == entry.SecretRef && secret.Identity == $"{module}-migrations-identity"))
                .Select(entry => entry.Name)
                .ToList();
            if (mainReadsTheInitStagesSecrets.Count > 0)
            {
                problems.Add($"{module}'s main container reads secrets resolved by {module}-migrations-identity: {string.Join(", ", mainReadsTheInitStagesSecrets)}.");
            }
        }

        problems.ShouldBeEmpty();
    }

    /// <summary>
    /// Published, a service's own container logs in as its module's role, <c>&lt;module&gt;_app</c>, never as the server's
    /// administrator (OQ8 c and Q1 on #114, docs/adr/0005). The Flexible Server's own module writes the role's connection
    /// string into the vault as <c>connectionstrings--&lt;module&gt;-database-app</c>, next to Aspire's administrator ones,
    /// with the password <c>guid(database_roles_key, '&lt;module&gt;')</c> derived from the deployment secret
    /// <c>database-roles-key</c>. Every service host is covered, found as the AppHost finds them. Keycloak's database gets a
    /// role's connection string too, for its own init container (Q2 on #114; AppHostKeycloakTests), and no other database does.
    /// </summary>
    [Fact]
    public async Task Every_service_logs_in_with_its_own_role()
    {
        await using var builder = await PublishedAppHost.ModelAsync(PublishedAppHost.Staging, TestContext.Current.CancellationToken);
        var modules = PublishedAppHost.Services(builder).Select(service => service.Name).ToList();

        var bicep = await PublishedAppHost.PublishAsync(PublishedAppHost.Staging, TestContext.Current.CancellationToken);

        modules.ShouldNotBeEmpty();
        var server = bicep.Module("postgres");
        List<string> problems = [];
        foreach (var module in modules)
        {
            var role = $"{module.Replace('-', '_')}_app";
            var secretName = RoleSecret(module);

            bicep.ContainerApps.ShouldContainKey(module);
            var app = bicep.ContainerApps[module];
            var secrets = PublishedBicep.SecretEntries(app);
            var main = PublishedBicep.Objects(PublishedBicep.Array(app, "containers")).SingleOrDefault(container => PublishedBicep.Property(container, "name") == module);
            var login = main is null ? null : PublishedBicep.EnvironmentOf(main).SingleOrDefault(entry => entry.Name == "ConnectionStrings__database");
            var secret = secrets.SingleOrDefault(entry => entry.Name == login?.SecretRef);
            if (login is null || login.SecretRef.Length == 0 || secret is null)
            {
                problems.Add($"{module}'s main container does not read ConnectionStrings__database from a secret of the app.");
            }
            else if (secret.VaultSecret != secretName || secret.Identity != $"{module}-identity")
            {
                problems.Add($"{module}'s main container reads ConnectionStrings__database from '{secret.VaultSecret}' under '{secret.Identity}', not from '{secretName}' under '{module}-identity'.");
            }

            var value = PublishedBicep.VaultSecretValue(server, secretName);
            if (value.Length == 0)
            {
                problems.Add($"postgres/postgres.bicep writes no Key Vault secret '{secretName}'.");
                continue;
            }

            var parts = value.Split(';');
            if (!parts.Contains("Host=${postgres.properties.fullyQualifiedDomainName}", StringComparer.Ordinal))
            {
                problems.Add($"'{secretName}' does not name the server as Host=${{postgres.properties.fullyQualifiedDomainName}}: {value}");
            }

            if (!parts.Contains($"Username={role}", StringComparer.Ordinal))
            {
                problems.Add($"'{secretName}' does not log in as Username={role}: {value}");
            }

            if (!parts.Contains($"Database={module}", StringComparer.Ordinal))
            {
                problems.Add($"'{secretName}' does not name Database={module}: {value}");
            }

            if (!Regex.IsMatch(value, $@"(^|;)Password=\$\{{guid\(database_roles_key,\s*'{Regex.Escape(module)}'\)\}}(;|$)", RegexOptions.None, TimeSpan.FromSeconds(1)))
            {
                problems.Add($"'{secretName}''s password is not ${{guid(database_roles_key, '{module}')}}: {value}");
            }

            if (value.Contains("administratorLogin", StringComparison.Ordinal))
            {
                problems.Add($"'{secretName}' logs in with the server's administrator login or password: {value}");
            }
        }

        if (!Regex.IsMatch(server, @"(?m)^@secure\(\)\s*\r?\n\s*param\s+database_roles_key\s+string\s*$", RegexOptions.None, TimeSpan.FromSeconds(1)))
        {
            problems.Add("postgres/postgres.bicep declares no @secure() parameter database_roles_key for the roles' passwords.");
        }

        var expected = modules.Append("keycloak").Select(RoleSecret).Order(StringComparer.Ordinal).ToList();
        var written = PublishedBicep.VaultSecretsWritten(server).Where(name => name.EndsWith("-database-app", StringComparison.Ordinal)).ToList();
        if (!written.SequenceEqual(expected, StringComparer.Ordinal))
        {
            problems.Add($"postgres/postgres.bicep should write a role's connection string for each service's module and for Keycloak, and no other database: {string.Join(", ", expected)}. It writes: {string.Join(", ", written)}");
        }

        problems.ShouldBeEmpty();
    }

    [Fact]
    public async Task Every_service_deploys_after_the_migrations_image()
    {
        await using var builder = await PublishedAppHost.ModelAsync(PublishedAppHost.Staging, TestContext.Current.CancellationToken);
        var services = PublishedAppHost.Services(builder);

        services.ShouldNotBeEmpty();
        foreach (var service in services)
        {
            var dependencies = await service.GetResourceDependenciesAsync(builder.ExecutionContext, ResourceDependencyDiscoveryMode.DirectOnly, TestContext.Current.CancellationToken);

            dependencies.Select(dependency => dependency.Name)
                .ShouldContain(Migrations, $"{service.Name} may deploy before the image of its init container is pushed.");
        }
    }

    /// <summary>A resource's environment as its model sets it, values unresolved.</summary>
    private static async Task<Dictionary<string, object>> EnvironmentAsync(IDistributedApplicationTestingBuilder builder, ProjectResource resource)
    {
        var context = new EnvironmentCallbackContext(builder.ExecutionContext, resource, cancellationToken: TestContext.Current.CancellationToken);
        foreach (var callback in resource.Annotations.OfType<EnvironmentCallbackAnnotation>())
        {
            await callback.Callback(context);
        }

        return context.EnvironmentVariables;
    }

    /// <summary>The resource whose connection string an environment value is, by name; empty for anything else.</summary>
    private static string ConnectionStringOf(object value) => value switch
    {
        ConnectionStringReference reference => reference.Resource.Name,
        IResourceWithConnectionString resource => resource.Name,
        _ => string.Empty,
    };

    /// <summary>The Key Vault secret holding a module's role's connection string, which the Flexible Server's module writes.</summary>
    private static string RoleSecret(string module) => $"connectionstrings--{module}-database-app";

    /// <summary>The user-assigned identities a module's resource runs under, as the parameters naming their ids.</summary>
    private static List<string> UserAssignedIdentities(string bicep) =>
        [.. Regex.Matches(PublishedBicep.Block(bicep, "userAssignedIdentities"), @"'\$\{(?<identity>\w+)\}'", RegexOptions.None, TimeSpan.FromSeconds(1))
            .Select(match => match.Groups["identity"].Value)];
}
