using System.Text.RegularExpressions;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure;
using Aspire.Hosting.Testing;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// In publish mode every secret is a parameter the deployment sets, never one Aspire generates or publishes a default for,
/// and it reaches Azure through the Key Vault <c>secrets</c>: each Container App reads it as a Key Vault reference, never
/// as a plain value. The model's endpoint and deployment reach every service as they do today, and the key is one of
/// those secrets (docs/adr/0005, docs/adr/0006).
/// </summary>
public class AppHostPublishParametersTests
{
    private const string KeyVault = "secrets";

    [Fact]
    public async Task The_published_model_has_one_key_vault_named_secrets()
    {
        await using var builder = await PublishedAppHost.ModelAsync(PublishedAppHost.Staging, TestContext.Current.CancellationToken);

        var vault = builder.Resources.OfType<AzureKeyVaultResource>().ShouldHaveSingleItem();

        vault.Name.ShouldBe(KeyVault);
    }

    [Theory]
    [InlineData("postgres-password")]
    [InlineData("keycloak-admin-password")]
    [InlineData("messaging-password")]
    [InlineData("azure-openai-api-key")]
    [InlineData("smtp-username")]
    [InlineData("smtp-password")]
    [InlineData("database-roles-key")]
    public async Task Every_publish_secret_is_an_explicit_parameter_in_key_vault(string name)
    {
        await using var builder = await PublishedAppHost.ModelAsync(PublishedAppHost.Staging, TestContext.Current.CancellationToken);

        var parameter = Parameter(builder, name);
        var secrets = builder.Resources.OfType<AzureKeyVaultSecretResource>().Where(secret => Holds(secret, parameter)).ToList();

        parameter.Secret.ShouldBeTrue($"{name} is not a secret parameter.");
        parameter.Default.ShouldBeNull($"{name} has a default ({parameter.Default?.GetType().Name}); the deployment must set it.");
        secrets.ShouldHaveSingleItem($"{name} is not a secret in a Key Vault.").Parent.Name.ShouldBe(KeyVault);
    }

    [Theory]
    [InlineData("postgres-username", null)]
    [InlineData("keycloak-admin-username", "admin")]
    [InlineData("messaging-username", null)]
    [InlineData("smtp-host", null)]
    [InlineData("smtp-from", null)]
    public async Task The_settings_beside_the_secrets_are_explicit_parameters(string name, string? defaultValue)
    {
        await using var builder = await PublishedAppHost.ModelAsync(PublishedAppHost.Staging, TestContext.Current.CancellationToken);

        var parameter = Parameter(builder, name);

        parameter.Secret.ShouldBeFalse($"{name} is not a secret.");
        if (defaultValue is null)
        {
            parameter.Default.ShouldBeNull($"{name} has a default; the deployment must set it.");
        }
        else
        {
            parameter.Default.ShouldNotBeNull($"{name} has no default.").GetDefaultValue().ShouldBe(defaultValue);
        }
    }

    /// <summary>Every service's own broker password (#183), found from the service hosts as the AppHost finds them.</summary>
    [Fact]
    public async Task Every_service_has_a_broker_password_of_its_own_as_an_explicit_parameter_in_key_vault()
    {
        await using var builder = await PublishedAppHost.ModelAsync(PublishedAppHost.Staging, TestContext.Current.CancellationToken);
        var services = PublishedAppHost.Services(builder);

        services.ShouldNotBeEmpty();
        foreach (var service in services)
        {
            var name = $"messaging-{service.Name}-password";
            var parameter = Parameter(builder, name);
            var secrets = builder.Resources.OfType<AzureKeyVaultSecretResource>().Where(secret => Holds(secret, parameter)).ToList();

            parameter.Secret.ShouldBeTrue($"{name} is not a secret parameter.");
            parameter.Default.ShouldBeNull($"{name} has a default ({parameter.Default?.GetType().Name}); the deployment must set it.");
            secrets.ShouldHaveSingleItem($"{name} is not a secret in a Key Vault.").Parent.Name.ShouldBe(KeyVault);
        }
    }

    /// <summary>
    /// Every service's broker user is named after its module, with no parameter to override it (docs/adr/0011): an
    /// overridden name could collide with another user's and take over its definitions entry.
    /// </summary>
    [Fact]
    public async Task No_services_broker_user_name_is_a_parameter()
    {
        await using var builder = await PublishedAppHost.ModelAsync(PublishedAppHost.Staging, TestContext.Current.CancellationToken);
        var services = PublishedAppHost.Services(builder);

        services.ShouldNotBeEmpty();
        foreach (var service in services)
        {
            builder.Resources.OfType<ParameterResource>().Select(parameter => parameter.Name)
                .ShouldNotContain($"messaging-{service.Name}-username", $"{service.Name}'s broker user name can be overridden.");
        }
    }

    /// <summary>
    /// The key every module role's password is derived from, <c>guid(database_roles_key, '&lt;module&gt;')</c>, is a secure
    /// parameter of main.bicep that the deployment sets, handed to the Flexible Server's module, which writes the roles'
    /// connection strings (Q1 on #114, docs/adr/0005).
    /// </summary>
    [Fact]
    public async Task The_database_roles_key_is_a_secure_parameter_the_server_receives()
    {
        var bicep = await PublishedAppHost.PublishAsync(PublishedAppHost.Staging, TestContext.Current.CancellationToken);
        var main = bicep.Files["main.bicep"];

        Regex.IsMatch(main, @"(?m)^@secure\(\)\s*\r?\n\s*param\s+database_roles_key\s+string\s*$", RegexOptions.None, TimeSpan.FromSeconds(1))
            .ShouldBeTrue("main.bicep declares no @secure() parameter database_roles_key without a default.");
        var postgres = main.IndexOf("module postgres 'postgres/postgres.bicep' = {", StringComparison.Ordinal);
        postgres.ShouldBeGreaterThanOrEqualTo(0, "main.bicep declares no module postgres.");
        var parameters = PublishedBicep.Block(main[postgres..], "params");
        Regex.IsMatch(parameters, @"(?m)^\s*database_roles_key:\s*database_roles_key\s*$", RegexOptions.None, TimeSpan.FromSeconds(1))
            .ShouldBeTrue($"main.bicep does not hand database_roles_key to the postgres module. It passes: {parameters}");
        PublishedBicep.SecureParameters(bicep.Module("postgres")).ShouldContain("database_roles_key", "postgres/postgres.bicep does not receive database_roles_key as a @secure() parameter.");
    }

    [Fact]
    public async Task No_container_app_receives_a_secret_as_a_plain_value()
    {
        var bicep = await PublishedAppHost.PublishAsync(PublishedAppHost.Staging, TestContext.Current.CancellationToken);

        bicep.ContainerApps.ShouldNotBeEmpty();
        foreach (var (name, module) in bicep.ContainerApps)
        {
            PublishedBicep.SecureParameters(module).ShouldBeEmpty($"{name} receives these secrets as values instead of Key Vault references.");
        }
    }

    [Fact]
    public async Task The_model_parameters_reach_every_service_and_the_key_is_a_key_vault_reference()
    {
        await using var builder = await PublishedAppHost.ModelAsync(PublishedAppHost.Staging, TestContext.Current.CancellationToken);
        var services = PublishedAppHost.Services(builder);

        services.ShouldNotBeEmpty();
        builder.Resources.ShouldAllBe(
            resource => !resource.GetType().Name.Contains("OpenAI", StringComparison.OrdinalIgnoreCase)
                && !resource.GetType().Name.Contains("CognitiveServices", StringComparison.OrdinalIgnoreCase),
            "Nothing provisions an Azure OpenAI resource.");
        foreach (var service in services)
        {
            var environment = await EnvironmentAsync(builder, service);

            environment["AzureOpenAI__Endpoint"].ShouldBeSameAs(Parameter(builder, "azure-openai-endpoint"), service.Name);
            environment["AzureOpenAI__Deployment"].ShouldBeSameAs(Parameter(builder, "azure-openai-deployment"), service.Name);
            var key = environment["AzureOpenAI__ApiKey"].ShouldBeAssignableTo<IAzureKeyVaultSecretReference>($"{service.Name} reads the model's key as a plain value.");
            key.ShouldNotBeNull();
            key.Resource.Name.ShouldBe(KeyVault);
        }
    }

    [Fact]
    public async Task The_mail_server_reaches_every_service_and_keycloak_as_key_vault_references()
    {
        await using var builder = await PublishedAppHost.ModelAsync(PublishedAppHost.Staging, TestContext.Current.CancellationToken);
        var services = PublishedAppHost.Services(builder);

        services.ShouldNotBeEmpty();
        foreach (var service in services)
        {
            var environment = await EnvironmentAsync(builder, service);

            var mail = environment["ConnectionStrings__mail"].ShouldBeAssignableTo<IAzureKeyVaultSecretReference>($"{service.Name} reads the mail server as a plain value.");
            mail.ShouldNotBeNull();
            mail.Resource.Name.ShouldBe(KeyVault);
        }

        var keycloak = builder.Resources.OfType<ContainerResource>().Single(resource => resource.Name == "keycloak");
        var keycloakEnvironment = await EnvironmentAsync(builder, keycloak);
        foreach (var name in new[] { "SMTP_USERNAME", "SMTP_PASSWORD" })
        {
            var secret = keycloakEnvironment[name].ShouldBeAssignableTo<IAzureKeyVaultSecretReference>($"Keycloak reads {name} as a plain value.");
            secret.ShouldNotBeNull();
            secret.Resource.Name.ShouldBe(KeyVault);
        }
    }

    private static ParameterResource Parameter(IDistributedApplicationTestingBuilder builder, string name)
    {
        var parameters = builder.Resources.OfType<ParameterResource>().ToList();
        return parameters.SingleOrDefault(parameter => parameter.Name == name)
            ?? throw new ShouldAssertException($"The published model has no parameter '{name}'. It has: {string.Join(", ", parameters.Select(parameter => parameter.Name).Order(StringComparer.Ordinal))}");
    }

    /// <summary>Whether a Key Vault secret holds the parameter's value, given as the parameter or an expression of it alone.</summary>
    private static bool Holds(AzureKeyVaultSecretResource secret, ParameterResource parameter) =>
        ReferenceEquals(secret.Value, parameter)
            || (secret.Value is ReferenceExpression expression
                && expression.Format == "{0}"
                && expression.ValueProviders.Count == 1
                && ReferenceEquals(expression.ValueProviders[0], parameter));

    /// <summary>The service's environment as the publish model sets it, values unresolved.</summary>
    private static async Task<Dictionary<string, object>> EnvironmentAsync(IDistributedApplicationTestingBuilder builder, IResource service)
    {
        var context = new EnvironmentCallbackContext(builder.ExecutionContext, service, cancellationToken: TestContext.Current.CancellationToken);
        foreach (var callback in service.Annotations.OfType<EnvironmentCallbackAnnotation>())
        {
            await callback.Callback(context);
        }

        return context.EnvironmentVariables;
    }
}
