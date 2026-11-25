using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure;
using Aspire.Hosting.Testing;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// Where published mail goes (#130, the owner's decisions on it). Staging sends to a Mailpit of its own inside the
/// Container Apps environment, whose inbox the staging end-to-end run reads behind Mailpit's basic auth: without TLS and
/// without credentials, since nothing there would trust a certificate Mailpit presents, and the environment encrypts the
/// traffic between its apps. Production sends through the deployment's relay (Azure Communication Services) on 587 with
/// STARTTLS and its credentials, as before, and has no Mailpit.
/// </summary>
public class AppHostStagingMailTests
{
    private const string Mailpit = "mailpit";

    private const string KeyVault = "secrets";

    [Fact]
    public async Task Staging_has_a_mailpit_of_its_own_and_production_has_none()
    {
        await using var staging = await PublishedAppHost.ModelAsync(PublishedAppHost.Staging, TestContext.Current.CancellationToken);
        await using var production = await PublishedAppHost.ModelAsync(PublishedAppHost.Production, TestContext.Current.CancellationToken);

        staging.Resources.OfType<ContainerResource>().Select(resource => resource.Name).ShouldContain(Mailpit);
        production.Resources.OfType<ContainerResource>().Select(resource => resource.Name).ShouldNotContain(Mailpit);
    }

    [Fact]
    public async Task Staging_mailpit_reads_its_inbox_password_from_the_key_vault_and_answers_smtp_inside_the_environment_alone()
    {
        await using var builder = await PublishedAppHost.ModelAsync(PublishedAppHost.Staging, TestContext.Current.CancellationToken);
        var mailpit = builder.Resources.OfType<ContainerResource>().Single(resource => resource.Name == Mailpit);

        var environment = await EnvironmentAsync(builder, mailpit);

        var auth = environment["MP_UI_AUTH"].ShouldBeAssignableTo<IAzureKeyVaultSecretReference>("Mailpit reads its inbox's basic auth as a plain value.");
        auth.ShouldNotBeNull().Resource.Name.ShouldBe(KeyVault);
        var endpoints = mailpit.Annotations.OfType<EndpointAnnotation>().ToDictionary(endpoint => endpoint.Name, StringComparer.Ordinal);
        endpoints["smtp"].IsExternal.ShouldBeFalse("Mailpit's SMTP port must answer inside the environment alone.");
        endpoints["http"].IsExternal.ShouldBeTrue("The staging run reads Mailpit's inbox from outside the environment, behind its basic auth.");
    }

    [Fact]
    public async Task Staging_services_send_to_mailpit_without_tls_and_production_services_through_the_vaulted_relay()
    {
        await using var staging = await PublishedAppHost.ModelAsync(PublishedAppHost.Staging, TestContext.Current.CancellationToken);
        await using var production = await PublishedAppHost.ModelAsync(PublishedAppHost.Production, TestContext.Current.CancellationToken);

        var stagingSenders = Senders(staging);
        stagingSenders.ShouldNotBeEmpty();
        foreach (var service in stagingSenders)
        {
            var environment = await EnvironmentAsync(staging, service);
            var mail = environment["ConnectionStrings__mail"].ShouldBeOfType<ReferenceExpression>($"{service.Name} does not send to Mailpit on staging.");
            mail.Format.ShouldStartWith("smtp://", Case.Sensitive);
            mail.ValueProviders.OfType<EndpointReferenceExpression>().ShouldContain(endpoint => endpoint.Endpoint.Resource.Name == Mailpit, service.Name);
            var unencrypted = environment["Mail__UnencryptedHost"].ShouldBeOfType<EndpointReferenceExpression>($"{service.Name} does not name Mailpit as its unencrypted host.");
            unencrypted.Endpoint.Resource.Name.ShouldBe(Mailpit);
        }

        foreach (var service in Senders(production))
        {
            var environment = await EnvironmentAsync(production, service);
            environment["ConnectionStrings__mail"].ShouldBeAssignableTo<IAzureKeyVaultSecretReference>($"{service.Name} reads the relay as a plain value.");
            environment.ShouldNotContainKey("Mail__UnencryptedHost", $"{service.Name} may send without TLS in production.");
        }
    }

    [Theory]
    [InlineData(PublishedAppHost.Staging, "1025", "false", "false")]
    [InlineData(PublishedAppHost.Production, "587", "true", "true")]
    public async Task Keycloak_sends_to_its_environments_server_with_the_security_that_server_takes(string name, string port, string starttls, string auth)
    {
        await using var builder = await PublishedAppHost.ModelAsync(name, TestContext.Current.CancellationToken);
        var keycloak = builder.Resources.OfType<ContainerResource>().Single(resource => resource.Name == "keycloak");

        var environment = await EnvironmentAsync(builder, keycloak);

        Text(environment["SMTP_PORT"]).ShouldBe(port);
        Text(environment["SMTP_STARTTLS"]).ShouldBe(starttls);
        Text(environment["SMTP_AUTH"]).ShouldBe(auth);
        if (name == PublishedAppHost.Production)
        {
            environment["SMTP_PASSWORD"].ShouldBeAssignableTo<IAzureKeyVaultSecretReference>("Keycloak reads the relay's password as a plain value.");
        }
        else
        {
            environment["SMTP_HOST"].ShouldBeOfType<EndpointReferenceExpression>("Keycloak does not send to Mailpit on staging.").Endpoint.Resource.Name.ShouldBe(Mailpit);
        }
    }

    [Fact]
    public async Task Staging_needs_no_relay_parameters_and_production_needs_them_all()
    {
        await using var staging = await PublishedAppHost.ModelAsync(PublishedAppHost.Staging, TestContext.Current.CancellationToken);
        await using var production = await PublishedAppHost.ModelAsync(PublishedAppHost.Production, TestContext.Current.CancellationToken);
        string[] relay = ["smtp-host", "smtp-from", "smtp-username", "smtp-password"];

        var stagingParameters = staging.Resources.OfType<ParameterResource>().Select(parameter => parameter.Name).ToList();
        var productionParameters = production.Resources.OfType<ParameterResource>().Select(parameter => parameter.Name).ToList();

        stagingParameters.ShouldNotContain(name => relay.Contains(name), "Staging sends to its Mailpit; it needs no relay.");
        stagingParameters.ShouldContain("mailpit-ui-auth");
        relay.ShouldAllBe(name => productionParameters.Contains(name));
        productionParameters.ShouldNotContain("mailpit-ui-auth");
    }

    private static List<ProjectResource> Senders(IDistributedApplicationTestingBuilder builder) =>
        PublishedAppHost.Services(builder).Where(service => ServiceHostMail.SendsMail(service.GetProjectMetadata().ProjectPath)).ToList();

    /// <summary>A setting given as a plain string, or as a parameter with a default.</summary>
    private static string? Text(object value) => value switch
    {
        string text => text,
        ParameterResource parameter => parameter.Default?.GetDefaultValue(),
        _ => throw new ShouldAssertException($"Expected a plain setting, found {value.GetType().Name}."),
    };

    /// <summary>The resource's environment as the publish model sets it, values unresolved.</summary>
    private static async Task<Dictionary<string, object>> EnvironmentAsync(IDistributedApplicationTestingBuilder builder, IResource resource)
    {
        var context = new EnvironmentCallbackContext(builder.ExecutionContext, resource, cancellationToken: TestContext.Current.CancellationToken);
        foreach (var callback in resource.Annotations.OfType<EnvironmentCallbackAnnotation>())
        {
            await callback.Callback(context);
        }

        return context.EnvironmentVariables.ToDictionary(variable => variable.Key, variable => variable.Value, StringComparer.Ordinal);
    }
}
