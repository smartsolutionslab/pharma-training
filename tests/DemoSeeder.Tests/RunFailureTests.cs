namespace SmartSolutionsLab.PharmaTraining.DemoSeeder.Tests;

/// <summary>
/// A step that fails with an exception the run did not expect ends the run with exit code 1, naming the step, never with
/// the runtime's 134 and a stack trace (review 6062523101 of #460): HttpClient's timeout (a
/// <see cref="TaskCanceledException"/> after 100 seconds), a <see cref="FormatException"/> from a number that is not one, a
/// <see cref="UriFormatException"/> from an address that is not one, or an <see cref="InvalidOperationException"/> from a
/// JSON value of another kind.
/// </summary>
public class RunFailureTests
{
    [Theory]
    [InlineData("timeout")]
    [InlineData("format")]
    [InlineData("uri")]
    [InlineData("invalid-operation")]
    public async Task An_unexpected_exception_in_an_edge_step_ends_the_run_with_exit_code_1_naming_the_step(string kind)
    {
        var harness = new SeederHarness();
        harness.App.ThrowOnce(HttpMethod.Post, "/organisation/pharmacies", Unexpected(kind));

        var exitCode = await harness.RunAsync(Plans.OnePharmacyOneCourse());

        var output = harness.Output.ToString();
        exitCode.ShouldBe(1, $"A {kind} failure should end the run with exit code 1. Output:\n{output}");
        output.ShouldContain("a step failed", Case.Sensitive);
        output.ShouldContain("/organisation/pharmacies", Case.Sensitive, "The failure names the step it happened in.");
    }

    [Theory]
    [InlineData("timeout")]
    [InlineData("format")]
    [InlineData("uri")]
    [InlineData("invalid-operation")]
    public async Task An_unexpected_exception_at_the_admin_api_ends_the_run_with_exit_code_1_naming_the_step(string kind)
    {
        var harness = new SeederHarness();
        harness.App.ThrowOnce(HttpMethod.Get, $"/admin/realms/{FakeKeycloak.Realm}/users", Unexpected(kind));

        var exitCode = await harness.RunAsync(Plans.OnePharmacyOneCourse());

        var output = harness.Output.ToString();
        exitCode.ShouldBe(1, $"A {kind} failure at the admin API should end the run with exit code 1. Output:\n{output}");
        output.ShouldContain("a step failed", Case.Sensitive);
        output.ShouldContain("/users", Case.Sensitive, "The failure names the step it happened in.");
        output.ShouldNotContain(FakeKeycloak.AdministratorPassword, Case.Sensitive);
    }

    private static Exception Unexpected(string kind) => kind switch
    {
        "timeout" => new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout of 100 seconds elapsing.", new TimeoutException("The operation was canceled.")),
        "format" => new FormatException("The input string 'many' was not in a correct format."),
        "uri" => new UriFormatException("Invalid URI: The format of the URI could not be determined."),
        "invalid-operation" => new InvalidOperationException("An element of type 'String' cannot be converted to 'System.Int32'."),
        _ => throw new InvalidOperationException($"No exception of the kind {kind}."),
    };
}
