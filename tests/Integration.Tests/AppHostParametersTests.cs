using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.Logging.Abstractions;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// The AppHost's model parameters, read from its application model, which is built and never started. A parameter
/// without a value makes Aspire's run mode hold back every resource that references it until someone sets it, so the
/// model's endpoint and key default to empty: the application starts without a model and says so when one is needed.
/// </summary>
public class AppHostParametersTests
{
    private const string Endpoint = "azure-openai-endpoint";

    private const string ApiKey = "azure-openai-api-key";

    private const string Deployment = "azure-openai-deployment";

    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(1);

    [Fact]
    public async Task The_model_parameters_resolve_without_a_value_so_nothing_waits_for_them()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(Timeout);
        await using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.AppHost>(timeout.Token);
        Unconfigured(builder);

        var endpoint = await Parameter(builder, Endpoint).GetValueAsync(timeout.Token);
        var key = Parameter(builder, ApiKey);
        var keyValue = await key.GetValueAsync(timeout.Token);
        var deployment = await Parameter(builder, Deployment).GetValueAsync(timeout.Token);

        endpoint.ShouldBe(string.Empty);
        keyValue.ShouldBe(string.Empty);
        key.Secret.ShouldBeTrue();
        deployment.ShouldBe("gpt-5-mini");
    }

    [Fact]
    public async Task A_configured_model_parameter_wins_over_its_default()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(Timeout);
        await using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.AppHost>(timeout.Token);
        Unconfigured(builder);
        builder.Configuration[$"Parameters:{Endpoint}"] = "https://configured-resource.openai.azure.com";
        builder.Configuration[$"Parameters:{ApiKey}"] = "configured-key";

        var endpoint = await Parameter(builder, Endpoint).GetValueAsync(timeout.Token);
        var key = await Parameter(builder, ApiKey).GetValueAsync(timeout.Token);

        endpoint.ShouldBe("https://configured-resource.openai.azure.com");
        key.ShouldBe("configured-key");
    }

    [Fact]
    public async Task The_mail_parameters_have_their_defaults_and_reach_every_service()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(Timeout);
        await using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.AppHost>(timeout.Token);
        Unconfigured(builder);

        var mailFrom = builder.Resources.OfType<ParameterResource>().SingleOrDefault(parameter => parameter.Name == "mail-from");
        mailFrom.ShouldNotBeNull("the AppHost has no parameter mail-from");
        (await mailFrom.GetValueAsync(timeout.Token)).ShouldBe("noreply@example.com");
        var mail = builder.Resources.OfType<IResourceWithConnectionString>().SingleOrDefault(resource => resource.Name == "mail");
        mail.ShouldNotBeNull("the AppHost has no connection string mail");
        mail.ConnectionStringExpression.ValueExpression.ShouldStartWith("smtp://", Case.Sensitive, "in run mode the services send to Mailpit's SMTP endpoint, without credentials");
        mail.ConnectionStringExpression.ValueExpression.ShouldContain("{mailpit.bindings.smtp.", Case.Sensitive);

        var services = Services(builder);
        services.ShouldNotBeEmpty();
        foreach (var service in services)
        {
            var configuration = await ExecutionConfigurationBuilder.Create(service)
                .WithEnvironmentVariablesConfig()
                .BuildAsync(new DistributedApplicationExecutionContext(DistributedApplicationOperation.Publish), NullLogger.Instance, timeout.Token);
            var environment = configuration.EnvironmentVariables.ToDictionary(variable => variable.Key, variable => variable.Value, StringComparer.Ordinal);

            environment.ShouldContainKey("ConnectionStrings__mail", $"{service.Name} does not read the connection string mail.");
            environment["ConnectionStrings__mail"].ShouldBe("{mail.connectionString}");
            environment.ShouldContainKey("Mail__From", $"{service.Name} does not read Mail:From.");
            environment["Mail__From"].ShouldBe("{mail-from.value}");
            environment.ShouldContainKey("Frontend__BaseUrl", $"{service.Name} does not read Frontend:BaseUrl.");
            environment["Frontend__BaseUrl"].ShouldContain("{web.bindings.http.", Case.Sensitive);
        }
    }

    [Fact]
    public async Task Deployed_mail_from_has_no_published_default_as_frontend_url_has_none()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(Timeout);
        await using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.AppHost>(["--operation", "publish"], timeout.Token);
        Unconfigured(builder);
        builder.ExecutionContext.IsPublishMode.ShouldBeTrue("the AppHost's model was not built in publish mode");

        var frontendUrl = Parameter(builder, "frontend-url");
        var mailFrom = Parameter(builder, "mail-from");

        frontendUrl.Default.ShouldBeNull("frontend-url publishes a default");
        mailFrom.Default.ShouldBeNull("mail-from publishes a default, so a deployment that forgot it sends from that address");
    }

    /// <summary>A developer's user secrets must not decide what these tests see.</summary>
    private static void Unconfigured(IDistributedApplicationTestingBuilder builder)
    {
        builder.Configuration[$"Parameters:{Endpoint}"] = null;
        builder.Configuration[$"Parameters:{ApiKey}"] = null;
    }

    private static ParameterResource Parameter(IDistributedApplicationTestingBuilder builder, string name) =>
        builder.Resources.OfType<ParameterResource>().Single(parameter => parameter.Name == name);

    /// <summary>Every module's service host, found as the AppHost finds them: src/Services/*/Host/Host.csproj.</summary>
    private static List<ProjectResource> Services(IDistributedApplicationTestingBuilder builder) =>
        [.. builder.Resources.OfType<ProjectResource>()
            .Where(project => IsServiceHost(project.GetProjectMetadata().ProjectPath))
            .OrderBy(project => project.Name, StringComparer.Ordinal)];

    private static bool IsServiceHost(string projectPath)
    {
        var host = Path.GetDirectoryName(projectPath);
        var service = Path.GetDirectoryName(host);
        var services = Path.GetDirectoryName(service);
        return Path.GetFileName(projectPath) == "Host.csproj"
            && Path.GetFileName(host) == "Host"
            && Path.GetFileName(services) == "Services";
    }
}
