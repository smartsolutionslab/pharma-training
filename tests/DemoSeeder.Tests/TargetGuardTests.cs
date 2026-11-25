namespace SmartSolutionsLab.PharmaTraining.DemoSeeder.Tests;

/// <summary>
/// The seeder runs against Local or Staging alone: any other target, Production included or absent, ends the run with
/// exit code 2 before any call, naming the key. Local seeds a loopback edge only; Staging only over https and never with
/// a password-based administrator.
/// </summary>
public class TargetGuardTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Production")]
    [InlineData("production")]
    [InlineData("Prod")]
    public async Task The_target_must_be_local_or_staging(string? target)
    {
        var harness = new SeederHarness();

        var exitCode = await harness.RunAsync(Plans.OnePharmacyOneCourse(), LocalSettings.Build(values => values["DemoSeeder:Target"] = target));

        exitCode.ShouldBe(2, $"Target {target ?? "(absent)"} should be refused. Output:\n{harness.Output}");
        harness.App.Requests.ShouldBeEmpty("A refused target makes no call at all.");
        harness.Output.ToString().ShouldContain("DemoSeeder:Target");
    }

    [Theory]
    [InlineData("https://pharma.example.com")]
    [InlineData("http://10.0.0.5:5100")]
    public async Task Local_refuses_an_edge_that_is_not_loopback(string edgeUrl)
    {
        var harness = new SeederHarness();

        var exitCode = await harness.RunAsync(Plans.OnePharmacyOneCourse(), LocalSettings.Build(values => values["DemoSeeder:EdgeUrl"] = edgeUrl));

        exitCode.ShouldBe(2, $"Local against {edgeUrl} should be refused. Output:\n{harness.Output}");
        harness.App.Requests.ShouldBeEmpty();
        harness.Output.ToString().ShouldContain("DemoSeeder:EdgeUrl");
    }

    [Fact]
    public async Task Staging_refuses_an_http_edge()
    {
        var harness = new SeederHarness();

        var exitCode = await harness.RunAsync(Plans.OnePharmacyOneCourse(), LocalSettings.Build(values => Staging(values, "http://staging.example.com")));

        exitCode.ShouldBe(2, $"Staging over http should be refused. Output:\n{harness.Output}");
        harness.App.Requests.ShouldBeEmpty();
        harness.Output.ToString().ShouldContain("DemoSeeder:EdgeUrl");
    }

    [Fact]
    public async Task Staging_refuses_a_password_administrator()
    {
        var harness = new SeederHarness();

        var exitCode = await harness.RunAsync(Plans.OnePharmacyOneCourse(), LocalSettings.Build(values =>
        {
            Staging(values, "https://staging.example.com");
            values["DemoSeeder:AdminUser"] = "admin";
            values["DemoSeeder:AdminPassword"] = "a-password";
        }));

        exitCode.ShouldBe(2, $"Staging with a password administrator should be refused. Output:\n{harness.Output}");
        harness.App.Requests.ShouldBeEmpty();
        harness.Output.ToString().ShouldContain("DemoSeeder:AdminPassword");
        harness.Output.ToString().ShouldNotContain("a-password", Case.Sensitive, "The refusal names the key, never its value.");
    }

    /// <summary>Settings as the staging workflow sets them: the client credentials and the users' password, no administrator.</summary>
    private static void Staging(Dictionary<string, string?> values, string edgeUrl)
    {
        values["DemoSeeder:Target"] = "Staging";
        values["DemoSeeder:EdgeUrl"] = edgeUrl;
        values["DemoSeeder:WebOrigin"] = edgeUrl;
        values["DemoSeeder:MailpitUrl"] = "https://mail.staging.example.com";
        values["DemoSeeder:ClientId"] = "demo-seeder";
        values["DemoSeeder:ClientSecret"] = "client-secret";
        values["DemoSeeder:UserPassword"] = "users-password";
        values.Remove("DemoSeeder:AdminUser");
        values.Remove("DemoSeeder:AdminPassword");
        values.Remove("DemoSeeder:KeycloakAdminUrl");
    }
}
