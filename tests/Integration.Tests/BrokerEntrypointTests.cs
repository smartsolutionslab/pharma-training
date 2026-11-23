namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// The broker's entrypoint (#183, docs/adr/0011), run in the broker's own image as the AppHost publishes it. It must fail
/// closed: every placeholder in the definitions is filled from a variable of the broker's own that is set, not empty and
/// URI-safe, or the broker does not boot, and the failure names the variable without ever printing its value. User names
/// are placeholders too, checked the same way, and no two users may share a name. Where it succeeds it hands the image's
/// entrypoint, here a stub that reports what it was given, the filled document and none of the variables that held it.
/// </summary>
public class BrokerEntrypointTests
{
    /// <summary>A document shaped like the AppHost's: the admin and one service, each name and password a placeholder.</summary>
    private const string Document =
        """{"users":[{"name":"@@RABBITMQ_DEFAULT_USER@@","password":"@@RABBITMQ_DEFAULT_PASS@@","tags":["administrator"]},""" +
        """{"name":"@@BROKER_USER_CATALOG@@","password":"@@BROKER_PASSWORD_CATALOG@@","tags":[]}],"permissions":[""" +
        """{"user":"@@RABBITMQ_DEFAULT_USER@@","vhost":"/","configure":".*","write":".*","read":".*"},""" +
        """{"user":"@@BROKER_USER_CATALOG@@","vhost":"/","configure":"^(catalog)$","write":"^(catalog)$","read":"^(catalog)$"}]}""";

    private const string AdminPassword = "Admin-Pass.1~_x";

    private const string CatalogPassword = "Catalog-Pass.2~_y";

    [Fact]
    public async Task A_complete_document_is_written_filled_in_and_the_variables_that_held_it_are_gone()
    {
        var run = await RunAsync(Valid());

        run.ExitCode.ShouldBe(0, run.Error);
        run.Arguments.ShouldBe("rabbitmq-server");
        run.Definitions.ShouldBe(
            Document
                .Replace("@@RABBITMQ_DEFAULT_USER@@", "admin", StringComparison.Ordinal)
                .Replace("@@RABBITMQ_DEFAULT_PASS@@", AdminPassword, StringComparison.Ordinal)
                .Replace("@@BROKER_USER_CATALOG@@", "catalog", StringComparison.Ordinal)
                .Replace("@@BROKER_PASSWORD_CATALOG@@", CatalogPassword, StringComparison.Ordinal));
        run.Variables.ShouldNotContain("BROKER_DEFINITIONS");
        run.Variables.ShouldNotContain("BROKER_PASSWORD_CATALOG");
        run.Variables.ShouldNotContain("BROKER_USER_CATALOG");
        run.Variables.ShouldNotContain("RABBITMQ_DEFAULT_PASS", "The admin's password stays in the broker's environment after the definitions are written.");
    }

    [Theory]
    [InlineData("BROKER_PASSWORD_CATALOG")]
    [InlineData("RABBITMQ_DEFAULT_PASS")]
    [InlineData("BROKER_USER_CATALOG")]
    [InlineData("RABBITMQ_DEFAULT_USER")]
    public async Task A_placeholder_whose_variable_is_not_set_fails_the_boot_naming_it(string variable)
    {
        var environment = Valid();
        environment.Remove(variable);

        var run = await RunAsync(environment);

        ShouldRefuse(run, variable, environment);
    }

    [Theory]
    [InlineData("BROKER_PASSWORD_CATALOG")]
    [InlineData("RABBITMQ_DEFAULT_PASS")]
    [InlineData("BROKER_USER_CATALOG")]
    [InlineData("RABBITMQ_DEFAULT_USER")]
    public async Task A_placeholder_whose_variable_is_empty_fails_the_boot_naming_it(string variable)
    {
        var environment = Valid();
        environment[variable] = string.Empty;

        var run = await RunAsync(environment);

        ShouldRefuse(run, variable, environment);
    }

    [Theory]
    [InlineData("BROKER_PASSWORD_CATALOG", "quote\"inside")]
    [InlineData("BROKER_PASSWORD_CATALOG", "back\\slash")]
    [InlineData("BROKER_PASSWORD_CATALOG", "with space")]
    [InlineData("BROKER_PASSWORD_CATALOG", "amp&ersand")]
    [InlineData("BROKER_PASSWORD_CATALOG", "colon:at@sign")]
    [InlineData("BROKER_PASSWORD_CATALOG", "pass@@RABBITMQ_DEFAULT_PASS@@word")]
    [InlineData("BROKER_PASSWORD_CATALOG", "umlaut-ä")]
    [InlineData("RABBITMQ_DEFAULT_PASS", "percent%2F")]
    [InlineData("RABBITMQ_DEFAULT_USER", "admin\",\"tags\":[\"administrator\"]},{\"name\":\"x")]
    [InlineData("BROKER_USER_CATALOG", "catalog/other")]
    public async Task A_value_with_a_character_outside_the_uri_safe_set_fails_the_boot_naming_its_variable(string variable, string value)
    {
        var environment = Valid();
        environment[variable] = value;

        var run = await RunAsync(environment);

        ShouldRefuse(run, variable, environment);
    }

    [Fact]
    public async Task A_placeholder_of_a_variable_that_is_not_the_brokers_fails_the_boot_naming_it()
    {
        var environment = Valid();
        environment["BROKER_DEFINITIONS"] = Document.Replace("@@BROKER_PASSWORD_CATALOG@@", "@@HOSTNAME@@", StringComparison.Ordinal);

        var run = await RunAsync(environment);

        ShouldRefuse(run, "HOSTNAME", environment);
    }

    [Fact]
    public async Task A_placeholder_left_in_the_document_fails_the_boot()
    {
        var environment = Valid();
        environment["BROKER_DEFINITIONS"] = Document.Replace("@@BROKER_PASSWORD_CATALOG@@", "@@BROKER-PASSWORD@@", StringComparison.Ordinal);

        var run = await RunAsync(environment);

        ShouldRefuse(run, "@@", environment);
    }

    [Fact]
    public async Task Two_users_with_one_name_fail_the_boot_naming_both_variables()
    {
        var environment = Valid();
        environment["RABBITMQ_DEFAULT_USER"] = "catalog";

        var run = await RunAsync(environment);

        ShouldRefuse(run, "RABBITMQ_DEFAULT_USER", environment);
        run.Error.ShouldContain("BROKER_USER_CATALOG");
    }

    private static Dictionary<string, string> Valid() => new(StringComparer.Ordinal)
    {
        ["BROKER_DEFINITIONS"] = Document,
        ["RABBITMQ_DEFAULT_USER"] = "admin",
        ["RABBITMQ_DEFAULT_PASS"] = AdminPassword,
        ["BROKER_USER_CATALOG"] = "catalog",
        ["BROKER_PASSWORD_CATALOG"] = CatalogPassword,
    };

    /// <summary>The boot failed, said which variable, never started RabbitMQ and printed none of the passwords.</summary>
    private static void ShouldRefuse(EntrypointRun run, string named, Dictionary<string, string> environment)
    {
        run.ExitCode.ShouldNotBe(0, $"The entrypoint started the broker. Its definitions: {run.Definitions}");
        run.Arguments.ShouldBeEmpty("The entrypoint started the broker.");
        run.Error.ShouldContain(named, Case.Sensitive, $"The failure does not name {named}: {run.Error}");
        foreach (var (variable, value) in environment.Where(variable => variable.Key.Contains("PASS", StringComparison.Ordinal) && variable.Value.Length > 0))
        {
            // Compared as a boolean, so a failure never prints the value.
            (run.Error.Contains(value, StringComparison.Ordinal) || run.Output.Contains(value, StringComparison.Ordinal)).ShouldBeFalse($"The entrypoint printed {variable}'s value.");
        }
    }

    private static async Task<EntrypointRun> RunAsync(Dictionary<string, string> environment)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (image, script) = await BrokerEntrypoint.PublishedAsync(cancellationToken);
        return await BrokerEntrypoint.RunAsync(image, script, environment, cancellationToken);
    }
}
