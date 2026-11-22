using System.Text.RegularExpressions;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure;
using Microsoft.Extensions.Hosting;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// Published, every app's identity reads the Key Vault secrets it references and no others, and traffic between the apps
/// in the Container Apps environment is encrypted (decisions OQ8 on #114, docs/adr/0005). A secret reaches an app only
/// through the vault: a secret parameter the vault does not hold is a mistake in the AppHost, never a plain value.
/// </summary>
public partial class AppHostSecretAccessTests
{
    private const string KeyVaultSecretsUser = "4633458b-17de-408a-b874-0445c86b69e6";

    private const string Vault = "Microsoft.KeyVault/vaults";

    private const string VaultSecret = "Microsoft.KeyVault/vaults/secrets";

    // Built-in roles that read or manage a vault's contents; any of them on a vault, a resource group or a subscription
    // reaches every secret in it.
    // A bare publish waits for a parameter without a value as the AppHost's does (PublishedAppHost).
    private static readonly TimeSpan PublishTimeout = TimeSpan.FromMinutes(2);

    private static readonly HashSet<string> KeyVaultDataRoles =
    [
        KeyVaultSecretsUser,
        "b86a8fe4-44ce-4948-aee5-eccb2c155cd7", // Key Vault Secrets Officer
        "00482a5a-887f-4fb3-b363-3b7fe8e74483", // Key Vault Administrator
        "21090545-7ca7-4776-b22c-e363652d74d2", // Key Vault Reader
        "a4417e6f-fecd-4de8-b567-7b0420556985", // Key Vault Certificates Officer
        "db79e9a7-68ee-4b58-9aeb-b90e7c24fcba", // Key Vault Certificate User
        "14b46e9e-c2b7-41b4-b07b-48a6ebf60603", // Key Vault Crypto Officer
        "12338af0-0e69-4776-bea7-57ae8d297424", // Key Vault Crypto User
    ];

    [Fact]
    public async Task Each_app_reads_the_secrets_it_references_and_no_others()
    {
        var bicep = await PublishedAppHost.PublishAsync(PublishedAppHost.Staging, TestContext.Current.CancellationToken);
        // An app may resolve its secrets under more than one identity (a service's init container reads the administrator's
        // connection string under <module>-migrations-identity); each identity is held to the secrets read under it.
        var apps = bicep.ContainerApps
            .SelectMany(app => PublishedBicep.SecretEntries(app.Value)
                .Where(entry => entry.VaultSecret.Length > 0)
                .GroupBy(entry => entry.Identity, StringComparer.Ordinal)
                .Select(byIdentity => (
                    Name: app.Key,
                    Identity: byIdentity.Key.Length > 0 ? byIdentity.Key : null,
                    Secrets: byIdentity.Select(entry => entry.VaultSecret).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList())))
            .ToList();

        apps.ShouldNotBeEmpty();
        foreach (var (name, identity, secrets) in apps)
        {
            identity.ShouldNotBeNull($"{name} reads Key Vault secrets under no identity of its own.");
            var assignments = bicep.RoleAssignmentsOf(identity).ToList();
            var described = Describe(assignments);

            assignments.Where(assignment => assignment.ScopeType == Vault)
                .ShouldBeEmpty($"{name} ({identity}) holds a role on the whole vault: {described}");
            assignments.Where(assignment => KeyVaultDataRoles.Contains(assignment.Role) && assignment.ScopeType != VaultSecret)
                .ShouldBeEmpty($"{name} ({identity}) holds a Key Vault data role beyond single secrets: {described}");
            assignments.Where(assignment => assignment.Role == KeyVaultSecretsUser && assignment.ScopeType == VaultSecret)
                .Select(assignment => assignment.ScopeName)
                .Order(StringComparer.Ordinal)
                .ToList()
                .ShouldBe(secrets, ignoreOrder: false, customMessage: $"{name} ({identity}) should be a Key Vault Secrets User on each secret it references, and those alone. It holds: {described}");
        }
    }

    [Fact]
    public async Task Only_migrations_and_keycloak_read_an_administrator_connection_string()
    {
        var bicep = await PublishedAppHost.PublishAsync(PublishedAppHost.Staging, TestContext.Current.CancellationToken);

        // A module database's connection string as the Flexible Server writes it, connectionstrings--<module>-database, logs
        // in as the server's administrator; a service logs in as its module's role instead (OQ8 c on #114, docs/adr/0005).
        // Keycloak still logs in as the administrator until it has a role of its own (Q2).
        var readers = bicep.Identities()
            .SelectMany(identity => bicep.RoleAssignmentsOf(identity)
                .Where(assignment => assignment.Role == KeyVaultSecretsUser && assignment.ScopeType == VaultSecret)
                .Where(assignment => AdministratorConnectionString().IsMatch(assignment.ScopeName))
                .Select(assignment => (Identity: identity, Secret: assignment.ScopeName)))
            .Distinct()
            .ToList();

        readers.ShouldNotBeEmpty("No identity reads an administrator connection string; the migrations could not log in.");
        readers.Where(reader => !reader.Identity.EndsWith("-migrations-identity", StringComparison.Ordinal) && reader.Identity != "keycloak-identity")
            .Select(reader => $"{reader.Identity} reads {reader.Secret}")
            .ShouldBeEmpty("Only an init stage's migrations identity, and Keycloak's, may read an administrator connection string.");
    }

    [Fact]
    public async Task No_identity_holds_a_role_on_the_whole_vault()
    {
        var bicep = await PublishedAppHost.PublishAsync(PublishedAppHost.Staging, TestContext.Current.CancellationToken);

        var onTheVault = bicep.AllRoleAssignments()
            .Where(assignment => assignment.ScopeType == Vault || (KeyVaultDataRoles.Contains(assignment.Role) && assignment.ScopeType != VaultSecret))
            .ToList();

        onTheVault.ShouldBeEmpty($"These reach every secret in the vault: {Describe(onTheVault)}");
    }

    [Theory]
    [InlineData(PublishedAppHost.Staging)]
    [InlineData(PublishedAppHost.Production)]
    public async Task Traffic_between_the_apps_is_encrypted(string environment)
    {
        var bicep = await PublishedAppHost.PublishAsync(environment, TestContext.Current.CancellationToken);

        var peerTraffic = PublishedBicep.Block(bicep.Module("aca"), "peerTrafficConfiguration");

        peerTraffic.ShouldNotBeEmpty("The Container Apps environment sets no peerTrafficConfiguration.");
        PublishedBicep.Block(peerTraffic, "encryption").ShouldMatch(@"\benabled:\s*true\b");
    }

    [Fact]
    public async Task Each_app_that_reads_secrets_deploys_after_its_own_secret_grants()
    {
        var bicep = await PublishedAppHost.PublishAsync(PublishedAppHost.Staging, TestContext.Current.CancellationToken);
        var main = bicep.Files["main.bicep"];
        var apps = bicep.ContainerApps.Where(app => PublishedBicep.KeyVaultSecretsReferenced(app.Value).Count > 0).ToList();

        apps.ShouldNotBeEmpty();
        List<string> unordered = [];
        foreach (var (name, module) in apps)
        {
            // The app's own grants, <app>-roles-secrets; an app is published as a deployment of its own, so it waits for
            // them only by taking one of their outputs as a parameter, which main.bicep outputs from that module.
            var roles = $"{name.Replace('-', '_')}_roles_secrets";
            main.ShouldMatch($@"(?m)^module\s+{roles}\s+'", $"{name} reads Key Vault secrets but main.bicep has no module {roles}.");
            var takesAnOutput = Regex.IsMatch(module, $@"(?mi)^param\s+{roles}_outputs_\w+\s", RegexOptions.None, TimeSpan.FromSeconds(1));
            var mainOutputsIt = Regex.IsMatch(main, $@"(?m)^output\s+{roles}_\w+\s+\w+\s*=\s*{roles}\.outputs\.\w+", RegexOptions.None, TimeSpan.FromSeconds(1));
            if (!takesAnOutput || !mainOutputsIt)
            {
                unordered.Add($"{name} (takes an output of {roles}: {takesAnOutput}; main.bicep outputs one: {mainOutputsIt})");
            }
        }

        unordered.ShouldBeEmpty("These apps may start before their identity may read the secrets they reference: each must take an output of its own <app>-roles-secrets module.");
    }

    [Theory]
    [InlineData(PublishedAppHost.Staging)]
    [InlineData(PublishedAppHost.Production)]
    public async Task The_container_apps_environment_keeps_api_version_2025_07_01_with_encrypted_peer_traffic(string environment)
    {
        var bicep = await PublishedAppHost.PublishAsync(environment, TestContext.Current.CancellationToken);
        var aca = bicep.Module("aca");

        var declared = Regex.Match(aca, @"'Microsoft\.App/managedEnvironments@(?<version>[^']+)'", RegexOptions.None, TimeSpan.FromSeconds(1));
        declared.Success.ShouldBeTrue("The aca module declares no Microsoft.App/managedEnvironments resource.");
        declared.Groups["version"].Value.ShouldBe("2025-07-01", "The Container Apps environment should be declared with API version 2025-07-01.");
        PublishedBicep.Block(PublishedBicep.Block(aca, "peerTrafficConfiguration"), "encryption").ShouldMatch(@"\benabled:\s*true\b");
    }

    [Fact]
    public async Task Published_a_secret_given_to_an_app_after_the_grants_is_granted_too()
    {
        var output = OutputDirectory();
        var builder = PublishBuilder(output);
        builder.AddContainerAppsEnvironment();
        var secrets = DeploymentSecrets.For(builder);
        var early = secrets.Secret("early-secret");
        var probe = builder.AddContainer("probe", "probe-image").WithSecret("Probe__Early", early, secrets);

        await secrets.GrantEachAppTheSecretsItReadsAsync(builder, TestContext.Current.CancellationToken);
        var late = secrets.Secret("late-secret");
        probe.WithSecret("Probe__Late", late, secrets);

        var bicep = await PublishAsync(builder, output, TestContext.Current.CancellationToken);

        bicep.ContainerApps.ShouldContainKey("probe");
        PublishedBicep.KeyVaultSecretsReferenced(bicep.ContainerApps["probe"]).ShouldBe(["early-secret", "late-secret"]);
        Granted(bicep, "probe").ShouldBe(["early-secret", "late-secret"], customMessage: $"probe should read each secret it references. It holds: {Describe(bicep.RoleAssignmentsOf("probe-identity"))}");
    }

    [Fact]
    public async Task Published_an_app_added_after_the_grants_is_granted_its_secrets()
    {
        var output = OutputDirectory();
        var builder = PublishBuilder(output);
        builder.AddContainerAppsEnvironment();
        var secrets = DeploymentSecrets.For(builder);

        await secrets.GrantEachAppTheSecretsItReadsAsync(builder, TestContext.Current.CancellationToken);
        var late = secrets.Secret("late-secret");
        builder.AddContainer("probe", "probe-image").WithSecret("Probe__Late", late, secrets);

        var bicep = await PublishAsync(builder, output, TestContext.Current.CancellationToken);

        bicep.ContainerApps.ShouldContainKey("probe");
        PublishedBicep.KeyVaultSecretsReferenced(bicep.ContainerApps["probe"]).ShouldBe(["late-secret"]);
        Granted(bicep, "probe").ShouldBe(["late-secret"], customMessage: $"probe should read the secret it references. It holds: {Describe(bicep.RoleAssignmentsOf("probe-identity"))}");
    }

    [Fact]
    public async Task Published_a_vault_secret_given_as_an_argument_is_granted()
    {
        var output = OutputDirectory();
        var builder = PublishBuilder(output);
        builder.AddContainerAppsEnvironment();
        var secrets = DeploymentSecrets.For(builder);
        var held = secrets.Holding(secrets.Secret("argument-secret")).ShouldNotBeNull();
        IAzureKeyVaultSecretReference reference = held.Resource;
        builder.AddContainer("probe", "probe-image").WithArgs(context => context.Args.Add(reference));

        await secrets.GrantEachAppTheSecretsItReadsAsync(builder, TestContext.Current.CancellationToken);
        var bicep = await PublishAsync(builder, output, TestContext.Current.CancellationToken);

        bicep.ContainerApps.ShouldContainKey("probe");
        Granted(bicep, "probe").ShouldBe(["argument-secret"], customMessage: $"probe is given argument-secret as an argument and should read it. It holds: {Describe(bicep.RoleAssignmentsOf("probe-identity"))}");
    }

    [Fact]
    public void Published_a_secret_parameter_the_vault_does_not_hold_fails_naming_it()
    {
        var builder = PublishBuilder();
        var secrets = DeploymentSecrets.For(builder);
        var stray = builder.AddParameter("stray-secret", secret: true);
        var probe = builder.AddContainer("probe", "probe-image");

        var failure = Should.Throw<InvalidOperationException>(() => probe.WithSecret("Probe__Secret", stray, secrets));

        failure.Message.ShouldContain("stray-secret");
    }

    [Fact]
    public void Published_a_secret_the_vault_holds_is_given_as_a_key_vault_reference()
    {
        var builder = PublishBuilder();
        var secrets = DeploymentSecrets.For(builder);
        var held = secrets.Secret("held-secret");
        var probe = builder.AddContainer("probe", "probe-image");

        Should.NotThrow(() => probe.WithSecret("Probe__Secret", held, secrets));
    }

    /// <summary>An empty application in publish mode, as <c>aspire publish</c> starts one; it is never built or run.</summary>
    private static IDistributedApplicationBuilder PublishBuilder() => PublishBuilder(OutputDirectory());

    /// <summary>An empty application in publish mode that writes its Bicep into <paramref name="output"/>.</summary>
    private static IDistributedApplicationBuilder PublishBuilder(string output) =>
        DistributedApplication.CreateBuilder(
            ["--operation", "publish", "--step", "publish", "--environment", PublishedAppHost.Staging, "--output-path", output, "--clear-cache", "true"]);

    private static string OutputDirectory() =>
        Path.Combine(Path.GetTempPath(), "pharma-training-publish", Guid.CreateVersion7().ToString("N"));

    /// <summary>
    /// Runs a bare application's publish pipeline and returns what it wrote; the directory is removed again. Every
    /// parameter without a value gets a placeholder, so the pipeline never waits for one.
    /// </summary>
    private static async Task<PublishedBicep> PublishAsync(IDistributedApplicationBuilder builder, string output, CancellationToken cancellationToken)
    {
        foreach (var parameter in builder.Resources.OfType<ParameterResource>().Where(parameter => parameter.Default is null))
        {
            builder.Configuration[$"Parameters:{parameter.Name}"] ??= $"placeholder-{parameter.Name}";
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(PublishTimeout);
            await using var app = builder.Build();
            await app.StartAsync(timeout.Token);
            await app.WaitForShutdownAsync(timeout.Token);
            return PublishedBicep.Read(output);
        }
        finally
        {
            if (Directory.Exists(output))
            {
                Directory.Delete(output, recursive: true);
            }
        }
    }

    /// <summary>The vault secrets an app's identity is a Key Vault Secrets User on, one secret at a time, by name.</summary>
    private static List<string> Granted(PublishedBicep bicep, string app) =>
        [.. bicep.RoleAssignmentsOf($"{app}-identity")
            .Where(assignment => assignment.Role == KeyVaultSecretsUser && assignment.ScopeType == VaultSecret)
            .Select(assignment => assignment.ScopeName)
            .Order(StringComparer.Ordinal)];

    private static string Describe(IEnumerable<PublishedRoleAssignment> assignments) =>
        string.Join("; ", assignments.Select(assignment => $"{assignment.Role} on {assignment.ScopeType} '{assignment.ScopeName}' ({assignment.File})"));

    [GeneratedRegex(@"^connectionstrings--.+-database$")]
    private static partial Regex AdministratorConnectionString();
}
