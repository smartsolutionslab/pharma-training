namespace SmartSolutionsLab.PharmaTraining.DemoSeeder.Tests;

/// <summary>
/// Every place the seeder sends a credential is checked before it is sent (review 6062523101 of #460). Under Local, the
/// master realm's administrator goes to <c>DemoSeeder:KeycloakAdminUrl</c>, Mailpit is read at <c>DemoSeeder:MailpitUrl</c>
/// and every demo user's password goes to the realm the edge's <c>/api/config.json</c> names (<c>oidcAuthority</c>): all
/// three must be loopback. Under Staging, the client's secret and the users' password go to that realm, which must be https
/// on the host of <c>DemoSeeder:KeycloakAdminUrl</c> (staging's Keycloak behind the auth proxy, itself https and required),
/// and Mailpit is read with its basic auth, <c>DemoSeeder:MailpitAuth</c>. A refusal is exit code 2, names the key and
/// never its value.
/// </summary>
public class CredentialDestinationTests
{
    private const string OtherHost = "auth.elsewhere.example";

    [Theory]
    [InlineData("https://auth.elsewhere.example")]
    [InlineData("http://10.0.0.5:8080")]
    public async Task Local_refuses_a_keycloak_admin_url_that_is_not_loopback(string keycloakAdminUrl)
    {
        var harness = new SeederHarness();

        var exitCode = await harness.RunAsync(Plans.OnePharmacyOneCourse(), LocalSettings.Build(values => values["DemoSeeder:KeycloakAdminUrl"] = keycloakAdminUrl));

        RefusedBeforeAnyCall(harness, exitCode, "DemoSeeder:KeycloakAdminUrl", new Uri(keycloakAdminUrl).Host);
        harness.Output.ToString().ShouldNotContain(FakeKeycloak.AdministratorPassword, Case.Sensitive);
    }

    [Theory]
    [InlineData("https://mail.elsewhere.example")]
    [InlineData("http://10.0.0.5:8025")]
    public async Task Local_refuses_a_mailpit_url_that_is_not_loopback(string mailpitUrl)
    {
        var harness = new SeederHarness();

        var exitCode = await harness.RunAsync(Plans.OnePharmacyOneCourse(), LocalSettings.Build(values => values["DemoSeeder:MailpitUrl"] = mailpitUrl));

        RefusedBeforeAnyCall(harness, exitCode, "DemoSeeder:MailpitUrl", new Uri(mailpitUrl).Host);
    }

    [Theory]
    [InlineData("https://auth.elsewhere.example/realms/pharma-training")]
    [InlineData("http://10.0.0.5:8080/realms/pharma-training")]
    public async Task Local_refuses_an_edge_whose_authority_is_not_loopback(string authority)
    {
        var harness = new SeederHarness();
        harness.App.Api.ReportedAuthority = authority;

        var exitCode = await harness.RunAsync(Plans.OnePharmacyOneCourse(), LocalSettings.Build());

        RefusedAfterTheConfiguration(harness, exitCode, "oidcAuthority", new Uri(authority).Host);
        harness.Output.ToString().ShouldNotContain(FakeKeycloak.AdministratorPassword, Case.Sensitive);
    }

    [Fact]
    public async Task Staging_refuses_an_authority_over_http()
    {
        var harness = StagingSettings.Harness();
        harness.App.Api.ReportedAuthority = StagingSettings.Authority.Replace("https://", "http://", StringComparison.Ordinal);

        var exitCode = await harness.RunAsync(Plans.OnePharmacyOneCourse(), StagingSettings.Build());

        RefusedAfterTheConfiguration(harness, exitCode, "oidcAuthority", null);
    }

    [Fact]
    public async Task Staging_refuses_an_authority_on_another_host_than_its_keycloak()
    {
        var harness = StagingSettings.Harness();
        harness.App.Api.ReportedAuthority = $"https://{OtherHost}/realms/pharma-training";

        var exitCode = await harness.RunAsync(Plans.OnePharmacyOneCourse(), StagingSettings.Build());

        RefusedAfterTheConfiguration(harness, exitCode, "oidcAuthority", OtherHost);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("http://auth.staging.example.com")]
    public async Task Staging_requires_an_https_keycloak_admin_url(string? keycloakAdminUrl)
    {
        var harness = StagingSettings.Harness();

        var exitCode = await harness.RunAsync(Plans.OnePharmacyOneCourse(), StagingSettings.Build(values => values["DemoSeeder:KeycloakAdminUrl"] = keycloakAdminUrl));

        RefusedBeforeAnyCall(harness, exitCode, "DemoSeeder:KeycloakAdminUrl", null);
    }

    [Fact]
    public async Task Staging_requires_mailpits_basic_auth()
    {
        var harness = StagingSettings.Harness();

        var exitCode = await harness.RunAsync(Plans.OnePharmacyOneCourse(), StagingSettings.Build(values => values.Remove("DemoSeeder:MailpitAuth")));

        RefusedBeforeAnyCall(harness, exitCode, "DemoSeeder:MailpitAuth", null);
    }

    private static void RefusedBeforeAnyCall(SeederHarness harness, int exitCode, string key, string? value)
    {
        var output = harness.Output.ToString();
        exitCode.ShouldBe(2, $"The settings should be refused, naming {key}. Output:\n{output}\nRequests:\n{string.Join('\n', harness.App.Requests)}");
        harness.App.Requests.ShouldBeEmpty("Refused settings make no call at all.");
        Named(output, key, value);
    }

    private static void RefusedAfterTheConfiguration(SeederHarness harness, int exitCode, string key, string? value)
    {
        var output = harness.Output.ToString();
        exitCode.ShouldBe(2, $"The edge's {key} should be refused. Output:\n{output}\nRequests:\n{string.Join('\n', harness.App.Requests)}");
        harness.App.Requests.Select(request => $"{request.Method} {request.Origin} {request.Path}").ShouldBe(
            ["GET edge /config.json"],
            "The edge's configuration is the one call before the refusal: no credential is sent anywhere.");
        output.ShouldNotContain(StagingSettings.ClientSecret, Case.Sensitive);
        output.ShouldNotContain(StagingSettings.UserPassword, Case.Sensitive);
        Named(output, key, value);
    }

    private static void Named(string output, string key, string? value)
    {
        output.ShouldContain(key, Case.Sensitive, "The refusal names the key.");
        if (value is not null)
        {
            output.ShouldNotContain(value, Case.Sensitive, "The refusal names the key, never its value.");
        }
    }
}
