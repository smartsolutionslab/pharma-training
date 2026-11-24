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
    public async Task The_mail_parameters_have_their_defaults_and_reach_the_services_that_send_mail()
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
        var senders = services.Where(SendsMail).ToList();
        senders.ShouldNotBeEmpty("no service host's project says <SendsMail>true</SendsMail>");
        services.Except(senders).ShouldNotBeEmpty("every service host's project says <SendsMail>true</SendsMail>");
        foreach (var service in senders)
        {
            var environment = await EnvironmentAsync(service, timeout.Token);

            environment.ShouldContainKey("ConnectionStrings__mail", $"{service.Name} sends mail but does not read the connection string mail.");
            environment["ConnectionStrings__mail"].ShouldBe("{mail.connectionString}");
            environment.ShouldContainKey("Mail__From", $"{service.Name} sends mail but does not read Mail:From.");
            environment["Mail__From"].ShouldBe("{mail-from.value}");
            environment.ShouldContainKey("Frontend__BaseUrl", $"{service.Name} sends mail but does not read Frontend:BaseUrl.");
            environment["Frontend__BaseUrl"].ShouldContain("{web.bindings.http.", Case.Sensitive);
        }
    }

    /// <summary>
    /// The mail settings go only to a service host whose project says it sends mail (docs/adr/0004); every other service
    /// has none of them, and the web app's address only where its project says <c>&lt;LinksToFrontend&gt;true&lt;/LinksToFrontend&gt;</c>
    /// (docs/adr/0030).
    /// </summary>
    [Fact]
    public async Task A_service_that_sends_no_mail_has_no_mail_connection_string_sender_or_frontend_url_in_run_mode()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(Timeout);
        await using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.AppHost>(timeout.Token);
        Unconfigured(builder);

        var services = Services(builder);
        var silent = services.Where(service => !SendsMail(service)).ToList();
        silent.ShouldNotBeEmpty("every service host's project says <SendsMail>true</SendsMail>");
        foreach (var service in silent)
        {
            var environment = await EnvironmentAsync(service, timeout.Token);

            environment.ShouldNotContainKey("ConnectionStrings__mail", $"{service.Name} sends no mail but reads the connection string mail.");
            environment.ShouldNotContainKey("Mail__From", $"{service.Name} sends no mail but reads Mail:From.");
            if (LinksToFrontend(service))
            {
                // A host that prints links into the web app without mailing gets its address alone (#50).
                environment.ShouldContainKey("Frontend__BaseUrl", $"{service.Name} says <LinksToFrontend>true</LinksToFrontend> but does not read Frontend:BaseUrl.");
                environment["Frontend__BaseUrl"].ShouldContain("{web.bindings.http.", Case.Sensitive);
            }
            else
            {
                environment.ShouldNotContainKey("Frontend__BaseUrl", $"{service.Name} sends no mail and links nowhere but reads Frontend:BaseUrl.");
            }
        }

        services.Where(SendsMail).ShouldNotBeEmpty("no service host's project says <SendsMail>true</SendsMail>");
    }

    [Fact]
    public async Task The_deployed_sender_has_no_published_default_as_frontend_url_has_none()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(Timeout);
        await using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.AppHost>(["--operation", "publish", "--environment", "staging"], timeout.Token);
        Unconfigured(builder);
        builder.ExecutionContext.IsPublishMode.ShouldBeTrue("the AppHost's model was not built in publish mode");

        // Published, the services' sender is the mail server's smtp-from, which Keycloak shares (docs/adr/0004).
        var frontendUrl = Parameter(builder, "frontend-url");
        var sender = Parameter(builder, "smtp-from");

        frontendUrl.Default.ShouldBeNull("frontend-url publishes a default");
        sender.Default.ShouldBeNull("smtp-from publishes a default, so a deployment that forgot it sends from that address");
    }

    private static bool SendsMail(ProjectResource service) => ServiceHostMail.SendsMail(service.GetProjectMetadata().ProjectPath);

    private static bool LinksToFrontend(ProjectResource service) => ServiceHostMail.LinksToFrontend(service.GetProjectMetadata().ProjectPath);

    /// <summary>The service's environment as the model sets it, values unresolved.</summary>
    private static async Task<Dictionary<string, string>> EnvironmentAsync(ProjectResource service, CancellationToken cancellationToken)
    {
        var configuration = await ExecutionConfigurationBuilder.Create(service)
            .WithEnvironmentVariablesConfig()
            .BuildAsync(new DistributedApplicationExecutionContext(DistributedApplicationOperation.Publish), NullLogger.Instance, cancellationToken);
        return configuration.EnvironmentVariables.ToDictionary(variable => variable.Key, variable => variable.Value, StringComparer.Ordinal);
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
