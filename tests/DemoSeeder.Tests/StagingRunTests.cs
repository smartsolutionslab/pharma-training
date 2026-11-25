namespace SmartSolutionsLab.PharmaTraining.DemoSeeder.Tests;

/// <summary>
/// A whole run against staging as the staging workflow starts it (#457 PR 2): the admin API is reached with the token of
/// the demo-seeder client's <c>client_credentials</c> grant, never a password-based administrator (owner decision 4);
/// staging's Mailpit is read with its basic auth, <c>DemoSeeder:MailpitAuth</c>; and every demo user's password is set to
/// <c>DemoSeeder:UserPassword</c> on every run, so rotating DEMO_USERS_PASSWORD takes effect at the next run (decision 5).
/// </summary>
public class StagingRunTests
{
    [Fact]
    public async Task Staging_administers_users_with_the_demo_seeder_clients_credentials()
    {
        var harness = Staging();

        await SeedAsync(harness);

        var grants = harness.App.Identity.TokenRequests.Where(form => form.GetValueOrDefault("grant_type") != "authorization_code").ToList();
        grants.ShouldNotBeEmpty("The run asked for no token of its own to administer users with.");
        grants.ShouldAllBe(form => form.GetValueOrDefault("grant_type") == "client_credentials" && form.GetValueOrDefault("client_id") == FakeKeycloak.SeederClientId);
        harness.App.Requests.ShouldNotContain(request => request.Path.StartsWith("/realms/master", StringComparison.Ordinal), "Staging never signs in to the master realm.");
        harness.App.Requests.ShouldNotContain(request => request.Body.Contains("grant_type=password", StringComparison.Ordinal), "Staging sends no password grant.");
        harness.App.Identity.Users.Select(user => user.Username).ShouldContain(Plans.Manager, "The admin API took the client's token and created the demo users.");
    }

    [Fact]
    public async Task Staging_reads_mailpit_with_its_basic_auth()
    {
        var harness = Staging();

        await SeedAsync(harness);

        var mail = harness.App.Requests.Where(request => request.Origin == "mailpit").ToList();
        mail.ShouldNotBeEmpty("The run read no invitation from Mailpit.");
        mail.ShouldAllBe(request => request.Status == 200, $"Every request to staging's Mailpit should carry its basic auth:\n{string.Join('\n', mail)}");
        harness.Output.ToString().ShouldNotContain(StagingSettings.MailpitAuth, Case.Sensitive);
    }

    [Fact]
    public async Task Staging_sets_every_demo_users_password_on_every_run()
    {
        var harness = Staging();
        harness.App.Identity.AddUser(Plans.Author, "a-password-of-an-earlier-secret", "editor");

        await SeedAsync(harness);
        UsersHoldThePassword(harness);

        foreach (var user in harness.App.Identity.Users)
        {
            user.Password = "rotated-away";
        }

        await SeedAsync(harness);
        UsersHoldThePassword(harness);
    }

    private static SeederHarness Staging()
    {
        var harness = StagingSettings.Harness();
        harness.App.Mail.RequiredAuth = StagingSettings.MailpitAuth;
        return harness;
    }

    private static async Task SeedAsync(SeederHarness harness)
    {
        var exitCode = await harness.RunAsync(Plans.OnePharmacyOneCourse(), StagingSettings.Build());
        exitCode.ShouldBe(0, $"The staging run failed. Output:\n{harness.Output}\nRequests:\n{string.Join('\n', harness.App.Requests)}");
    }

    private static void UsersHoldThePassword(SeederHarness harness)
    {
        var users = harness.App.Identity.Users.Where(user => user.Username.StartsWith("demo-", StringComparison.Ordinal)).ToList();
        users.ShouldNotBeEmpty();
        users.Where(user => user.Password != StagingSettings.UserPassword).Select(user => user.Username)
            .ShouldBeEmpty("Every demo user's password is DemoSeeder:UserPassword after a staging run.");
    }
}
