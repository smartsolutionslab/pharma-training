namespace SmartSolutionsLab.PharmaTraining.DemoSeeder.Tests;

/// <summary>
/// The production guard (#457 PR 2, review 6062523101 of #460): production's edge is https as staging's is, so with the
/// target Staging the seeder asks the edge which deployment it is before it sends any credential. Its anonymous
/// <c>/api/config.json</c> reports <c>environment</c>, the name the AppHost published it for; anything but <c>staging</c>
/// ends the run with exit code 2 after that one call, before the client's secret reaches Keycloak or a demo user's password
/// a login form, naming the field and never its value.
/// </summary>
public class ProductionGuardTests
{
    [Theory]
    [InlineData("production")]
    [InlineData("Production")]
    [InlineData("Development")]
    [InlineData("")]
    [InlineData(null)]
    public async Task Staging_refuses_an_edge_that_does_not_report_staging_before_any_credential_is_sent(string? environment)
    {
        var harness = StagingSettings.Harness();
        harness.App.Api.ReportedEnvironment = environment;

        var exitCode = await harness.RunAsync(Plans.OnePharmacyOneCourse(), StagingSettings.Build());

        var output = harness.Output.ToString();
        exitCode.ShouldBe(2, $"An edge reporting {environment ?? "(no environment)"} should be refused with the target Staging. Output:\n{output}\nRequests:\n{string.Join('\n', harness.App.Requests)}");
        harness.App.Requests.Select(request => $"{request.Method} {request.Origin} {request.Path}").ShouldBe(
            ["GET edge /config.json"],
            "The edge's configuration is the one call before the refusal: no token is asked for with the client's secret, no user signs in, no mail is read.");
        output.ShouldContain("environment", Case.Sensitive, "The refusal names the field of /api/config.json it refused.");
        output.ShouldNotContain(StagingSettings.ClientSecret, Case.Sensitive);
        output.ShouldNotContain(StagingSettings.UserPassword, Case.Sensitive);
        if (!string.IsNullOrEmpty(environment))
        {
            output.ShouldNotContain(environment, Case.Sensitive, "The refusal names the field, never its value.");
        }
    }

    /// <summary>
    /// Control, passing before and after the change: with everything as staging's workflow sets it and an edge that reports
    /// staging, the guard lets the run go on to Keycloak, so the refusals above are the guard's and not a broken setup.
    /// </summary>
    [Fact]
    public async Task Staging_goes_on_to_keycloak_once_the_edge_reports_staging()
    {
        var harness = StagingSettings.Harness();

        await harness.RunAsync(Plans.OnePharmacyOneCourse(), StagingSettings.Build());

        harness.App.Requests.ShouldContain(
            request => request.Origin == "keycloak",
            $"An edge reporting staging should let the run reach Keycloak. Output:\n{harness.Output}\nRequests:\n{string.Join('\n', harness.App.Requests)}");
    }
}
