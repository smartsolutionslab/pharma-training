using Microsoft.Extensions.Configuration;

namespace SmartSolutionsLab.PharmaTraining.DemoSeeder.Tests;

/// <summary>
/// The settings the staging workflow gives the seeder (demo-seed-staging.yml), pointing at the fake's staging origins:
/// target Staging, the edge (vars.FRONTEND_URL) as edge and web origin, staging's Mailpit behind its basic auth, Keycloak
/// behind the auth proxy (vars.X1_KEYCLOAK_URL), whose host the edge's authority must name, the demo-seeder client and
/// the demo users' password. No administrator. The fake edge reports the deployment staging and that authority.
/// </summary>
internal static class StagingSettings
{
    public const string ClientSecret = "client-secret-of-the-run";

    public const string UserPassword = "users-password-of-the-run";

    public const string MailpitAuth = "mailpit:auth-of-the-run";

    public static string Authority => $"{FakeApplication.StagingKeycloakUrl.ToString().TrimEnd('/')}/realms/{FakeKeycloak.Realm}";

    /// <summary>Turns the local settings into staging's.</summary>
    public static void Apply(Dictionary<string, string?> values)
    {
        values["DemoSeeder:Target"] = "Staging";
        values["DemoSeeder:EdgeUrl"] = FakeApplication.StagingEdgeUrl.ToString();
        values["DemoSeeder:WebOrigin"] = FakeApplication.StagingEdgeUrl.ToString();
        values["DemoSeeder:MailpitUrl"] = FakeApplication.StagingMailpitUrl.ToString();
        values["DemoSeeder:MailpitAuth"] = MailpitAuth;
        values["DemoSeeder:KeycloakAdminUrl"] = FakeApplication.StagingKeycloakUrl.ToString();
        values["DemoSeeder:ClientId"] = "demo-seeder";
        values["DemoSeeder:ClientSecret"] = ClientSecret;
        values["DemoSeeder:UserPassword"] = UserPassword;
        values.Remove("DemoSeeder:AdminUser");
        values.Remove("DemoSeeder:AdminPassword");
    }

    public static IConfiguration Build(Action<Dictionary<string, string?>>? change = null) => LocalSettings.Build(values =>
    {
        Apply(values);
        change?.Invoke(values);
    });

    /// <summary>A harness whose fake edge answers as staging's does: the deployment staging, the realm behind the auth proxy.</summary>
    public static SeederHarness Harness()
    {
        var harness = new SeederHarness();
        harness.App.Api.ReportedEnvironment = "staging";
        harness.App.Api.ReportedAuthority = Authority;
        return harness;
    }
}
