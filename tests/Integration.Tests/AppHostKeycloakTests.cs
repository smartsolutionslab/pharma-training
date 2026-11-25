using System.Text.RegularExpressions;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// Keycloak in both modes (PR-C on #114, docs/adr/0005). In run mode it is what it always was: Keycloak's own image in
/// <c>start-dev</c>, importing the development realm from <c>./realms</c> and keeping its data on a volume, with the edge
/// and every service validating tokens from its own endpoint. Published, it is an image built from that one with the
/// published realm (<c>src/AppHost/keycloak/realm.json</c>), started optimized; it keeps its data in its own database on the
/// Flexible Server, in the schema <c>keycloak</c>, logged in as the role <c>keycloak_app</c>, which the MigrationRunner
/// ensures as its init container; and the browser reaches it through the <c>auth</c> proxy alone, at
/// <c>https://auth.&lt;the environment's default domain&gt;</c>, which forwards the realm <c>pharma-training</c> and the
/// static resources and nothing else. That address is the issuer the edge and every service validate.
/// </summary>
public partial class AppHostKeycloakTests
{
    private const string Keycloak = "keycloak";

    private const string Auth = "auth";

    private const string Migrations = "migrations";

    private const string Image = "quay.io/keycloak/keycloak";

    private const string Version = "26.6";

    private const string DefaultDomain = "${aca_outputs_azure_container_apps_environment_default_domain}";

    private static readonly TimeSpan ModelTimeout = TimeSpan.FromMinutes(1);

    [Fact]
    public async Task Run_mode_keeps_start_dev_the_development_realm_and_a_data_volume()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(ModelTimeout);
        await using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.AppHost>(timeout.Token);

        var keycloak = builder.Resources.OfType<ContainerResource>().SingleOrDefault(resource => resource.Name == Keycloak);
        keycloak.ShouldNotBeNull("Run mode has no keycloak container.");
        var image = keycloak.Annotations.OfType<ContainerImageAnnotation>().ShouldHaveSingleItem();
        $"{(string.IsNullOrEmpty(image.Registry) ? string.Empty : $"{image.Registry}/")}{image.Image}:{image.Tag}".ShouldBe($"{Image}:{Version}");
        keycloak.Annotations.OfType<DockerfileBuildAnnotation>().ShouldBeEmpty("Run mode builds no Keycloak image; it runs Keycloak's own.");
        (await ArgumentsAsync(keycloak)).Select(argument => $"{argument}").ShouldBe(["start-dev", "--import-realm"]);

        var mounts = keycloak.Annotations.OfType<ContainerMountAnnotation>().ToList();
        var realms = mounts.Where(mount => mount.Type == ContainerMountType.BindMount).ShouldHaveSingleItem("Run mode should bind-mount the development realms alone.");
        Path.GetFileName(Path.TrimEndingDirectorySeparator(realms.Source.ShouldNotBeNull())).ShouldBe("realms");
        Path.GetFileName(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(realms.Source))).ShouldBe("AppHost");
        realms.Target.ShouldBe("/opt/keycloak/data/import");
        realms.IsReadOnly.ShouldBeTrue("The development realms are mounted writable.");
        var data = mounts.Where(mount => mount.Type == ContainerMountType.Volume).ShouldHaveSingleItem("Run mode should keep Keycloak's data on one volume.");
        data.Target.ShouldBe("/opt/keycloak/data");

        builder.Resources.Select(resource => resource.Name).ShouldNotContain(Auth, "Run mode has an auth proxy; the browser reaches Keycloak's own endpoint.");

        var host = builder.Resources.OfType<ProjectResource>().Single(resource => resource.Name == "host");
        var authority = (await EnvironmentAsync(builder.ExecutionContext, host))["Authentication__Authority"].ShouldBeOfType<ReferenceExpression>();
        authority.Format.ShouldBe("{0}/realms/pharma-training");
        var endpoint = authority.ValueProviders.ShouldHaveSingleItem().ShouldBeOfType<EndpointReference>();
        endpoint.Resource.Name.ShouldBe(Keycloak);
        endpoint.EndpointName.ShouldBe("http");
    }

    /// <summary>
    /// C3: the Flexible Server's own module writes Keycloak's role's password and connection string into the vault beside the
    /// modules' (Q1 and Q2 on #114): <c>keycloak-database-password</c>, which Keycloak's own container reads as
    /// <c>KC_DB_PASSWORD</c>, and <c>connectionstrings--keycloak-database-app</c>, from which its init container ensures the
    /// role. Both derive the password from <c>database-roles-key</c>, as every module role's does.
    /// </summary>
    [Fact]
    public async Task The_server_writes_keycloaks_role_password_and_connection_string_into_the_vault()
    {
        var bicep = await PublishedAppHost.PublishAsync(PublishedAppHost.Staging, TestContext.Current.CancellationToken);
        var server = bicep.Module("postgres");
        const string password = "keycloak-database-password";
        const string connectionString = "connectionstrings--keycloak-database-app";
        const string derived = "${guid(database_roles_key, 'keycloak')}";

        List<string> problems = [];
        var written = PublishedBicep.VaultSecretsWritten(server);
        var passwordValue = PublishedBicep.VaultSecretValue(server, password);
        if (passwordValue.Length == 0)
        {
            problems.Add($"postgres/postgres.bicep writes no Key Vault secret '{password}'. It writes: {string.Join(", ", written)}");
        }
        else if (passwordValue is not ("guid(database_roles_key, 'keycloak')" or derived))
        {
            problems.Add($"'{password}' should hold guid(database_roles_key, 'keycloak') and nothing else: {passwordValue}");
        }

        var value = PublishedBicep.VaultSecretValue(server, connectionString);
        if (value.Length == 0)
        {
            problems.Add($"postgres/postgres.bicep writes no Key Vault secret '{connectionString}'. It writes: {string.Join(", ", written)}");
        }
        else
        {
            var parts = value.Split(';');
            foreach (var expected in new[] { "Host=${postgres.properties.fullyQualifiedDomainName}", "Username=keycloak_app", $"Password={derived}", "Database=keycloak" })
            {
                if (!parts.Contains(expected, StringComparer.Ordinal))
                {
                    problems.Add($"'{connectionString}' does not hold {expected}: {value}");
                }
            }
        }

        foreach (var (name, secret) in new[] { (password, passwordValue), (connectionString, value) })
        {
            if (secret.Contains("administratorLogin", StringComparison.Ordinal))
            {
                problems.Add($"'{name}' holds the server's administrator login or password: {secret}");
            }
        }

        problems.ShouldBeEmpty();
    }

    /// <summary>
    /// C4 (K2): published, Keycloak runs an image built from Keycloak's own, optimized for PostgreSQL with health checks
    /// and holding the published realm, from the build context <c>src/AppHost/keycloak</c>.
    /// </summary>
    [Fact]
    public async Task The_published_image_is_built_with_the_published_realm()
    {
        await using var builder = await PublishedAppHost.ModelAsync(PublishedAppHost.Staging, TestContext.Current.CancellationToken);
        var keycloak = builder.Resources.OfType<ContainerResource>().Single(resource => resource.Name == Keycloak);

        var build = keycloak.Annotations.OfType<DockerfileBuildAnnotation>().SingleOrDefault();
        build.ShouldNotBeNull("Published, Keycloak runs Keycloak's own image; it should be built with WithDockerfileBuilder from ./keycloak.");
        var context = Path.TrimEndingDirectorySeparator(build.ContextPath);
        Path.GetFileName(context).ShouldBe(Keycloak, $"Keycloak's image is built from {context}, not from src/AppHost/keycloak.");
        Path.GetFileName(Path.GetDirectoryName(context)).ShouldBe("AppHost", $"Keycloak's image is built from {context}, not from src/AppHost/keycloak.");
        File.Exists(Path.Combine(context, "realm.json")).ShouldBeTrue($"The build context {context} holds no realm.json.");
        build.DockerfileFactory.ShouldNotBeNull("Keycloak's Dockerfile should be written by the AppHost (WithDockerfileBuilder), not kept as a file.");

        await using var app = await builder.BuildAsync(TestContext.Current.CancellationToken);
        var dockerfile = await build.DockerfileFactory(new DockerfileFactoryContext
        {
            Services = app.Services,
            Resource = keycloak,
            CancellationToken = TestContext.Current.CancellationToken,
        });

        var stages = FromLine().Matches(dockerfile).Select(match => match.Groups["image"].Value).ToList();
        stages.Count.ShouldBeGreaterThanOrEqualTo(2, $"Keycloak's Dockerfile should build in one stage and run in another:\n{dockerfile}");
        stages.ShouldAllBe(stage => stage == $"{Image}:{Version}" || stage.StartsWith($"{Image}:{Version}@sha256:", StringComparison.Ordinal), $"Every stage of Keycloak's Dockerfile should start from {Image}:{Version}:\n{dockerfile}");
        dockerfile.ShouldMatch(@"(?m)^RUN\s+/opt/keycloak/bin/kc\.sh\s+build\b.*--db=postgres\b", $"Keycloak's Dockerfile does not run kc.sh build --db=postgres:\n{dockerfile}");
        dockerfile.ShouldMatch(@"(?m)^RUN\s+/opt/keycloak/bin/kc\.sh\s+build\b.*--health-enabled=true\b", $"Keycloak's Dockerfile does not build with --health-enabled=true:\n{dockerfile}");
        dockerfile.ShouldMatch(@"(?m)^COPY\s+--from=\S+\s+/opt/keycloak/?\s+/opt/keycloak/?\s*$", $"Keycloak's Dockerfile does not copy the built /opt/keycloak into the stage it runs:\n{dockerfile}");
        dockerfile.ShouldMatch(@"(?m)^COPY\s+realm\.json\s+/opt/keycloak/data/import/realm\.json\s*$", $"Keycloak's Dockerfile does not copy the published realm to /opt/keycloak/data/import/realm.json:\n{dockerfile}");
        dockerfile.ShouldMatch(@"(?m)^COPY\s+--chmod=0?755\s+kcadm-otp\.sh\s+/opt/keycloak/bin/", $"Keycloak's Dockerfile should make kcadm-otp.sh executable itself (#331), not rely on the mode the checkout gave it:\n{dockerfile}");
    }

    /// <summary>
    /// The security review of #114 (PR-C): a tag can be moved to an image nobody reviewed, so every stage of Keycloak's
    /// published image starts from Keycloak's image by its digest, beside the tag that names its version for people and
    /// Dependabot.
    /// </summary>
    [Fact]
    public async Task Keycloaks_published_image_starts_from_a_digest()
    {
        await using var builder = await PublishedAppHost.ModelAsync(PublishedAppHost.Staging, TestContext.Current.CancellationToken);
        var keycloak = builder.Resources.OfType<ContainerResource>().Single(resource => resource.Name == Keycloak);
        var build = keycloak.Annotations.OfType<DockerfileBuildAnnotation>().Single();
        build.DockerfileFactory.ShouldNotBeNull("Keycloak's Dockerfile should be written by the AppHost.");
        await using var app = await builder.BuildAsync(TestContext.Current.CancellationToken);

        var dockerfile = await build.DockerfileFactory(new DockerfileFactoryContext
        {
            Services = app.Services,
            Resource = keycloak,
            CancellationToken = TestContext.Current.CancellationToken,
        });

        var stages = FromLine().Matches(dockerfile).Select(match => match.Groups["image"].Value).ToList();
        stages.ShouldNotBeEmpty($"Keycloak's Dockerfile has no FROM:\n{dockerfile}");
        stages.Where(stage => !PinnedImage().IsMatch(stage)).ShouldBeEmpty($"Every FROM of Keycloak's Dockerfile should name {Image}:{Version}@sha256:<64 hex characters>, not a tag alone, which can be moved:\n{dockerfile}");
        stages.ShouldAllBe(stage => stage.StartsWith($"{Image}:{Version}@", StringComparison.Ordinal), $"Every FROM of Keycloak's Dockerfile should keep the tag {Version} beside the digest:\n{dockerfile}");
    }

    /// <summary>
    /// The security review of #114 (PR-C): the <c>auth</c> proxy, the browser's one way to Keycloak, runs YARP's image by
    /// its digest. Aspire's default is a nightly preview tag, which moves with every build.
    /// </summary>
    [Fact]
    public async Task The_auth_proxy_runs_its_image_by_digest()
    {
        var bicep = await PublishedAppHost.PublishAsync(PublishedAppHost.Staging, TestContext.Current.CancellationToken);
        bicep.ContainerApps.ShouldContainKey(Auth);

        var images = PublishedBicep.Objects(PublishedBicep.Array(bicep.ContainerApps[Auth], "containers"))
            .Select(container => PublishedBicep.Property(container, "image"))
            .ToList();

        images.ShouldNotBeEmpty($"The auth proxy's Container App has no container:\n{bicep.ContainerApps[Auth]}");
        images.Where(image => !DigestImage().IsMatch(image)).ShouldBeEmpty("The auth proxy's container should run its image by digest, <image>@sha256:<64 hex characters> (WithImageSHA256); a tag alone can be moved to an image nobody reviewed.");
    }

    [Theory]
    [InlineData(PublishedAppHost.Staging)]
    [InlineData(PublishedAppHost.Production)]
    public async Task Published_keycloak_starts_optimized_and_imports_the_realm(string environment)
    {
        var bicep = await PublishedAppHost.PublishAsync(environment, TestContext.Current.CancellationToken);

        Arguments(MainContainer(bicep)).ShouldBe(["start", "--optimized", "--import-realm"], "Published, Keycloak should run its built image with start --optimized --import-realm, never start-dev.");
    }

    /// <summary>
    /// C4 (K3): published, Keycloak logs in to its own database on the Flexible Server, over TLS, as <c>keycloak_app</c>,
    /// with the password from the vault, and keeps its tables in the schema <c>keycloak</c>; it has no volume and no H2.
    /// </summary>
    [Fact]
    public async Task Published_keycloak_logs_in_to_its_own_database_as_its_role()
    {
        var bicep = await PublishedAppHost.PublishAsync(PublishedAppHost.Staging, TestContext.Current.CancellationToken);
        var app = bicep.ContainerApps[Keycloak];
        var environment = PublishedBicep.EnvironmentOf(MainContainer(bicep));
        var secrets = PublishedBicep.SecretEntries(app);

        List<string> problems = [];
        foreach (var (name, expected) in new[]
        {
            ("KC_DB_URL_HOST", "postgres_outputs_hostname"),
            ("KC_DB_URL_DATABASE", "keycloak"),
            ("KC_DB_URL_PROPERTIES", "?sslmode=require"),
            ("KC_DB_SCHEMA", "keycloak"),
            ("KC_DB_USERNAME", "keycloak_app"),
            ("KC_HTTP_ENABLED", "true"),
            ("KC_PROXY_HEADERS", "xforwarded"),
        })
        {
            var entry = environment.SingleOrDefault(candidate => candidate.Name == name);
            if (entry is null || entry.Value != expected)
            {
                problems.Add($"Keycloak's {name} should be {expected}; it is {(entry is null ? "missing" : $"'{entry.Value}'{(entry.SecretRef.Length > 0 ? $" (secretRef {entry.SecretRef})" : string.Empty)}")}.");
            }
        }

        var password = environment.SingleOrDefault(entry => entry.Name == "KC_DB_PASSWORD");
        var passwordSecret = secrets.SingleOrDefault(entry => entry.Name == password?.SecretRef);
        if (password is null || password.SecretRef.Length == 0 || passwordSecret is null)
        {
            problems.Add("Keycloak does not read KC_DB_PASSWORD from a secret of the app.");
        }
        else if (passwordSecret.VaultSecret != "keycloak-database-password" || passwordSecret.Identity != "keycloak-identity")
        {
            problems.Add($"Keycloak reads KC_DB_PASSWORD from '{passwordSecret.VaultSecret}' under '{passwordSecret.Identity}', not from 'keycloak-database-password' under 'keycloak-identity'.");
        }

        if (environment.Any(entry => entry.Name == "KC_DB_URL"))
        {
            problems.Add("Keycloak is given KC_DB_URL; it should be built from KC_DB_URL_HOST, KC_DB_URL_DATABASE and KC_DB_URL_PROPERTIES, which hold no password.");
        }

        if (environment.Any(entry => entry.Value.Contains("Password=", StringComparison.OrdinalIgnoreCase) || entry.Value.Contains("administratorLogin", StringComparison.Ordinal)))
        {
            problems.Add("A variable of Keycloak's holds a password or the server's administrator login as a plain value.");
        }

        if (Regex.IsMatch(app, @"\bvolumes:|\bvolumeMounts:", RegexOptions.None, TimeSpan.FromSeconds(1)))
        {
            problems.Add("Published, Keycloak still mounts a volume; its data lives in its database.");
        }

        problems.ShouldBeEmpty();
    }

    /// <summary>
    /// C5 (K4): before Keycloak starts, the MigrationRunner's image ensures its role and schema as Keycloak's init container,
    /// in the role-only mode (<c>Migrations__OwnedSchema</c>, no module), reading the administrator's connection string and
    /// the role's under an identity no container can get a token for, as every service's init container does.
    /// </summary>
    [Fact]
    public async Task Keycloak_ensures_its_role_first_in_an_init_container()
    {
        var bicep = await PublishedAppHost.PublishAsync(PublishedAppHost.Staging, TestContext.Current.CancellationToken);
        bicep.ContainerApps.ShouldContainKey(Keycloak);
        var app = bicep.ContainerApps[Keycloak];

        List<string> problems = [];
        var initContainers = PublishedBicep.Objects(PublishedBicep.Array(app, "initContainers"));
        if (initContainers.Count != 1)
        {
            problems.Add($"Keycloak has {initContainers.Count} init containers, not one.");
        }
        else
        {
            var init = initContainers[0];
            var secrets = PublishedBicep.SecretEntries(app);
            var environment = PublishedBicep.EnvironmentOf(init);
            if (PublishedBicep.Property(init, "name") != Migrations)
            {
                problems.Add($"Keycloak's init container is named '{PublishedBicep.Property(init, "name")}', not '{Migrations}'.");
            }

            if (PublishedBicep.Property(init, "image") != $"{Migrations}_containerimage")
            {
                problems.Add($"Keycloak's init container runs '{PublishedBicep.Property(init, "image")}', not the migrations job's image ({Migrations}_containerimage).");
            }

            if (environment.SingleOrDefault(entry => entry.Name == "Migrations__OwnedSchema") is not { Value: "keycloak" })
            {
                problems.Add("Keycloak's init container does not name the schema it ensures in Migrations__OwnedSchema=keycloak.");
            }

            if (environment.Any(entry => entry.Name == "Migrations__Module"))
            {
                problems.Add("Keycloak's init container names a module in Migrations__Module; Keycloak's database has no module's migrations.");
            }

            foreach (var (variable, vaultSecret) in new[] { ("ConnectionStrings__database", "connectionstrings--keycloak-database"), ("ConnectionStrings__application", "connectionstrings--keycloak-database-app") })
            {
                var entry = environment.SingleOrDefault(candidate => candidate.Name == variable);
                var secret = secrets.SingleOrDefault(candidate => candidate.Name == entry?.SecretRef);
                if (entry is null || entry.SecretRef.Length == 0 || secret is null)
                {
                    problems.Add($"Keycloak's init container does not read {variable} from a secret of the app.");
                }
                else if (secret.VaultSecret != vaultSecret || secret.Identity != "keycloak-migrations-identity")
                {
                    problems.Add($"Keycloak's init container reads {variable} from '{secret.VaultSecret}' under '{secret.Identity}', not from '{vaultSecret}' under 'keycloak-migrations-identity'.");
                }
            }

            var mainReadsTheInitStagesSecrets = PublishedBicep.EnvironmentOf(MainContainer(bicep))
                .Where(entry => entry.SecretRef.Length > 0)
                .Where(entry => secrets.Any(secret => secret.Name == entry.SecretRef && secret.Identity == "keycloak-migrations-identity"))
                .Select(entry => entry.Name)
                .ToList();
            if (mainReadsTheInitStagesSecrets.Count > 0)
            {
                problems.Add($"Keycloak's main container reads secrets resolved by keycloak-migrations-identity: {string.Join(", ", mainReadsTheInitStagesSecrets)}.");
            }
        }

        var settings = PublishedBicep.Objects(PublishedBicep.Array(PublishedBicep.Block(app, "configuration"), "identitySettings"))
            .ToDictionary(setting => PublishedBicep.Property(setting, "identity"), setting => PublishedBicep.Property(setting, "lifecycle"), StringComparer.Ordinal);
        if (settings.GetValueOrDefault("keycloak_migrations_identity_outputs_id") != "None")
        {
            problems.Add($"keycloak-migrations-identity has lifecycle '{settings.GetValueOrDefault("keycloak_migrations_identity_outputs_id")}', not 'None': a container could get a token for it.");
        }

        if (settings.GetValueOrDefault("keycloak_identity_outputs_id") != "Main")
        {
            problems.Add($"keycloak-identity has lifecycle '{settings.GetValueOrDefault("keycloak_identity_outputs_id")}', not 'Main'.");
        }

        problems.ShouldBeEmpty();
    }

    [Fact]
    public async Task Keycloak_deploys_after_the_migrations_image()
    {
        await using var builder = await PublishedAppHost.ModelAsync(PublishedAppHost.Staging, TestContext.Current.CancellationToken);
        var keycloak = builder.Resources.OfType<ContainerResource>().Single(resource => resource.Name == Keycloak);

        var dependencies = await keycloak.GetResourceDependenciesAsync(builder.ExecutionContext, ResourceDependencyDiscoveryMode.DirectOnly, TestContext.Current.CancellationToken);

        dependencies.Select(dependency => dependency.Name).ShouldContain(Migrations, "Keycloak may deploy before the image of its init container is pushed.");
    }

    /// <summary>
    /// C6 (K8, X2): the <c>auth</c> proxy forwards the realm <c>pharma-training</c> and the static resources to Keycloak's
    /// <c>http</c> endpoint, and of the admin API only the realm's users and roles (#457, for the demo seeder's and X1's
    /// manage-users clients), and nothing else: not <c>/realms/master</c>, whose token endpoint accepts the administrator's
    /// password, not the rest of <c>/admin</c> (the console, other realms), not the management port.
    /// </summary>
    [Fact]
    public async Task Only_the_pharma_training_realm_and_resources_reach_keycloak()
    {
        await using var builder = await PublishedAppHost.ModelAsync(PublishedAppHost.Staging, TestContext.Current.CancellationToken);

        var auth = builder.Resources.SingleOrDefault(resource => resource.Name == Auth);
        auth.ShouldNotBeNull("Published, there is no auth proxy; the browser cannot reach Keycloak.");
        var environment = await EnvironmentAsync(builder.ExecutionContext, auth);

        var routes = environment
            .Select(variable => (Variable: variable, Match: RoutePath().Match(variable.Key)))
            .Where(route => route.Match.Success)
            .ToDictionary(route => route.Match.Groups["route"].Value, route => $"{route.Variable.Value}", StringComparer.Ordinal);
        routes.Values.Order(StringComparer.Ordinal).ShouldBe(
            [
                "/admin/realms/pharma-training/roles/{**catch-all}",
                "/admin/realms/pharma-training/users/{**catch-all}",
                "/realms/pharma-training/{**catch-all}",
                "/resources/{**catch-all}",
            ],
            ignoreOrder: false,
            $"The auth proxy should forward exactly the realm pharma-training, its admin users and roles, and the static resources. Its routes: {string.Join(", ", routes.Select(route => $"{route.Key} {route.Value}"))}");

        foreach (var route in routes.Keys)
        {
            var cluster = $"{environment.GetValueOrDefault($"REVERSEPROXY__ROUTES__{route}__CLUSTERID")}";
            var destinations = environment
                .Where(variable => variable.Key.StartsWith($"REVERSEPROXY__CLUSTERS__{cluster}__DESTINATIONS__", StringComparison.Ordinal) && variable.Key.EndsWith("__ADDRESS", StringComparison.Ordinal))
                .Select(variable => variable.Value)
                .ToList();
            destinations.ShouldNotBeEmpty($"The auth proxy's route {route} ({routes[route]}) leads to the cluster '{cluster}', which has no destination.");
            destinations.ShouldAllBe(
                destination => IsKeycloaksHttpEndpoint(destination),
                $"The auth proxy's route {route} ({routes[route]}) should lead to Keycloak's http endpoint alone.");
        }
    }

    [Fact]
    public async Task The_auth_proxy_trusts_the_forwarded_headers_of_the_ingress()
    {
        await using var builder = await PublishedAppHost.ModelAsync(PublishedAppHost.Staging, TestContext.Current.CancellationToken);
        var auth = builder.Resources.SingleOrDefault(resource => resource.Name == Auth);
        auth.ShouldNotBeNull("Published, there is no auth proxy; the browser cannot reach Keycloak.");

        var environment = await EnvironmentAsync(builder.ExecutionContext, auth);

        $"{environment.GetValueOrDefault("ASPNETCORE_FORWARDEDHEADERS_ENABLED")}".ShouldBe("true", "The auth proxy should read the scheme and host the Container Apps ingress forwards (ASPNETCORE_FORWARDEDHEADERS_ENABLED=true).");
    }

    /// <summary>
    /// C6 (K9, X5): published, the edge and every service validate tokens from the public issuer,
    /// <c>https://auth.&lt;default domain&gt;/realms/pharma-training</c>, the address the browser signs in at, never
    /// Keycloak's internal one, which no browser reaches.
    /// </summary>
    [Theory]
    [InlineData(PublishedAppHost.Staging)]
    [InlineData(PublishedAppHost.Production)]
    public async Task Every_host_validates_tokens_from_the_public_issuer(string environment)
    {
        await using var builder = await PublishedAppHost.ModelAsync(environment, TestContext.Current.CancellationToken);
        List<string> hosts = [.. PublishedAppHost.Services(builder).Select(service => service.Name).Prepend("host")];

        var bicep = await PublishedAppHost.PublishAsync(environment, TestContext.Current.CancellationToken);

        var expected = $"https://auth.{DefaultDomain}/realms/pharma-training";
        List<string> problems = [];
        foreach (var host in hosts)
        {
            bicep.ContainerApps.ShouldContainKey(host);
            var main = PublishedBicep.Objects(PublishedBicep.Array(bicep.ContainerApps[host], "containers"))
                .Single(container => PublishedBicep.Property(container, "name") == host);
            var authority = PublishedBicep.EnvironmentOf(main).SingleOrDefault(entry => entry.Name == "Authentication__Authority");
            if (authority is null || Flatten(authority.Value) != expected)
            {
                problems.Add($"{host}'s Authentication__Authority should be {expected}; it is {authority?.Value ?? "missing"}.");
            }
        }

        problems.ShouldBeEmpty();
    }

    /// <summary>
    /// C6 (K3, X5): Keycloak names the public address it issues tokens under, <c>https://auth.&lt;default domain&gt;</c>,
    /// and the realm's <c>${WEB_ORIGIN}</c> is the edge's, <c>https://host.&lt;default domain&gt;</c>, both from the
    /// environment's default domain, which exists before the realm is imported once, never from <c>frontend-url</c>.
    /// </summary>
    [Theory]
    [InlineData(PublishedAppHost.Staging)]
    [InlineData(PublishedAppHost.Production)]
    public async Task Published_keycloak_names_its_public_address_and_the_web_origin(string environment)
    {
        var bicep = await PublishedAppHost.PublishAsync(environment, TestContext.Current.CancellationToken);
        var variables = PublishedBicep.EnvironmentOf(MainContainer(bicep));

        List<string> problems = [];
        foreach (var (name, expected) in new[] { ("KC_HOSTNAME", $"https://auth.{DefaultDomain}"), ("WEB_ORIGIN", $"https://host.{DefaultDomain}") })
        {
            var entry = variables.SingleOrDefault(candidate => candidate.Name == name);
            if (entry is null || Flatten(entry.Value) != expected)
            {
                problems.Add($"Keycloak's {name} should be {expected}; it is {entry?.Value ?? "missing"}.");
            }
        }

        problems.ShouldBeEmpty();
    }

    /// <summary>Keycloak's own container in its published module.</summary>
    private static string MainContainer(PublishedBicep bicep)
    {
        bicep.ContainerApps.ShouldContainKey(Keycloak);
        return PublishedBicep.Objects(PublishedBicep.Array(bicep.ContainerApps[Keycloak], "containers"))
            .SingleOrDefault(container => PublishedBicep.Property(container, "name") == Keycloak)
            ?? throw new ShouldAssertException("Keycloak's module has no container named 'keycloak'.");
    }

    /// <summary>A container's <c>args</c>, each quoted literal unquoted.</summary>
    private static List<string> Arguments(string container) =>
        [.. Regex.Matches(PublishedBicep.Array(container, "args"), @"'(?<argument>[^']*)'", RegexOptions.None, TimeSpan.FromSeconds(1)).Select(match => match.Groups["argument"].Value)];

    /// <summary>
    /// A Bicep string value with its nested interpolations flattened: <c>${'https://a.${x}'}/b</c> is <c>https://a.${x}/b</c>,
    /// so an address reads the same however the expressions that make it nest.
    /// </summary>
    private static string Flatten(string value) => value.Replace("${'", string.Empty, StringComparison.Ordinal).Replace("'}", string.Empty, StringComparison.Ordinal);

    /// <summary>Whether an environment value is Keycloak's <c>http</c> endpoint, as a reference or an expression of it alone.</summary>
    private static bool IsKeycloaksHttpEndpoint(object value) => value switch
    {
        EndpointReference endpoint => endpoint.Resource.Name == Keycloak && endpoint.EndpointName == "http",
        EndpointReferenceExpression expression => expression.Endpoint.Resource.Name == Keycloak && expression.Endpoint.EndpointName == "http",
        ReferenceExpression expression => expression.ValueProviders.Count == 1 && IsKeycloaksHttpEndpoint(expression.ValueProviders[0]),
        _ => false,
    };

    /// <summary>A resource's command-line arguments as its model sets them, values unresolved.</summary>
    private static async Task<List<object>> ArgumentsAsync(ContainerResource resource)
    {
        List<object> arguments = [];
        var context = new CommandLineArgsCallbackContext(arguments, resource, TestContext.Current.CancellationToken);
        foreach (var callback in resource.Annotations.OfType<CommandLineArgsCallbackAnnotation>())
        {
            await callback.Callback(context);
        }

        return arguments;
    }

    /// <summary>A resource's environment as its model sets it, values unresolved.</summary>
    private static async Task<Dictionary<string, object>> EnvironmentAsync(DistributedApplicationExecutionContext executionContext, IResource resource)
    {
        var context = new EnvironmentCallbackContext(executionContext, resource, cancellationToken: TestContext.Current.CancellationToken);
        foreach (var callback in resource.Annotations.OfType<EnvironmentCallbackAnnotation>())
        {
            await callback.Callback(context);
        }

        return context.EnvironmentVariables;
    }

    [GeneratedRegex(@"(?m)^FROM\s+(?<image>\S+)")]
    private static partial Regex FromLine();

    /// <summary>An image named by its tag and its digest: <c>registry/name:tag@sha256:&lt;64 hex characters&gt;</c>.</summary>
    [GeneratedRegex(@"^'?[^\s'@]+:[^\s'@/]+@sha256:[0-9a-f]{64}'?$")]
    private static partial Regex PinnedImage();

    /// <summary>An image named by its digest, with or without a tag: <c>registry/name[:tag]@sha256:&lt;64 hex characters&gt;</c>.</summary>
    [GeneratedRegex(@"^'?[^\s'@]+@sha256:[0-9a-f]{64}'?$")]
    private static partial Regex DigestImage();

    [GeneratedRegex(@"^REVERSEPROXY__ROUTES__(?<route>.+)__MATCH__PATH$")]
    private static partial Regex RoutePath();
}
