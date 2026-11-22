using System.Text.RegularExpressions;
using Aspire.Hosting;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// Published, every app's identity reads the Key Vault secrets it references and no others, and traffic between the apps
/// in the Container Apps environment is encrypted (decisions OQ8 on #114, docs/adr/0005). A secret reaches an app only
/// through the vault: a secret parameter the vault does not hold is a mistake in the AppHost, never a plain value.
/// </summary>
public class AppHostSecretAccessTests
{
    private const string KeyVaultSecretsUser = "4633458b-17de-408a-b874-0445c86b69e6";

    private const string Vault = "Microsoft.KeyVault/vaults";

    private const string VaultSecret = "Microsoft.KeyVault/vaults/secrets";

    // Built-in roles that read or manage a vault's contents; any of them on a vault, a resource group or a subscription
    // reaches every secret in it.
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
        var apps = bicep.ContainerApps
            .Select(app => (Name: app.Key, Identity: Identity(app.Value), Secrets: PublishedBicep.KeyVaultSecretsReferenced(app.Value)))
            .Where(app => app.Secrets.Count > 0)
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

    /// <summary>The identity an app reads its Key Vault secrets under, as its module names it (<c>&lt;app&gt;-identity</c>).</summary>
    private static string? Identity(string app) =>
        Regex.Match(app, @"identity:\s*(?<identity>\w+?)_outputs_id\s+keyVaultUrl:", RegexOptions.None, TimeSpan.FromSeconds(1)) is { Success: true } match
            ? match.Groups["identity"].Value.Replace('_', '-')
            : null;

    /// <summary>An empty application in publish mode, as <c>aspire publish</c> starts one; it is never built or run.</summary>
    private static IDistributedApplicationBuilder PublishBuilder() =>
        DistributedApplication.CreateBuilder(
            ["--operation", "publish", "--step", "publish", "--environment", PublishedAppHost.Staging, "--output-path", Path.Combine(Path.GetTempPath(), "pharma-training-publish", Guid.CreateVersion7().ToString("N"))]);

    private static string Describe(IEnumerable<PublishedRoleAssignment> assignments) =>
        string.Join("; ", assignments.Select(assignment => $"{assignment.Role} on {assignment.ScopeType} '{assignment.ScopeName}' ({assignment.File})"));
}
