using Microsoft.Extensions.Configuration;

namespace SmartSolutionsLab.PharmaTraining.DemoSeeder.Tests;

/// <summary>
/// The settings the AppHost gives the seeder in run mode, pointing at the fake: target Local, the edge, the web app's
/// origin, Mailpit and Keycloak's own endpoint with the master realm's administrator. No user password: locally a demo
/// user's password is their username. Polls give up after an hour of the fake clock unless a test says otherwise.
/// </summary>
internal static class LocalSettings
{
    public static Dictionary<string, string?> Values() => new(StringComparer.Ordinal)
    {
        ["DemoSeeder:Target"] = "Local",
        ["DemoSeeder:EdgeUrl"] = FakeApplication.EdgeUrl.ToString(),
        ["DemoSeeder:WebOrigin"] = FakeApplication.WebOrigin.ToString(),
        ["DemoSeeder:MailpitUrl"] = FakeApplication.MailpitUrl.ToString(),
        ["DemoSeeder:KeycloakAdminUrl"] = FakeApplication.KeycloakUrl.ToString(),
        ["DemoSeeder:AdminUser"] = FakeKeycloak.Administrator,
        ["DemoSeeder:AdminPassword"] = FakeKeycloak.AdministratorPassword,
        ["DemoSeeder:PollTimeout"] = "01:00:00",
    };

    public static IConfiguration Build(Action<Dictionary<string, string?>>? change = null)
    {
        var values = Values();
        change?.Invoke(values);
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }
}
