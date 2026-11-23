using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure;
using Aspire.Hosting.Testing;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// In publish mode RabbitMQ is a container whose data outlives a restart on a named volume, with the user and password the
/// deployment sets, and its management endpoint stays inside the environment. Every service connects as a user of its own
/// (#183): the broker imports the users at every boot, through an entrypoint of its own, from definitions that carry no
/// secret (each password is the placeholder <c>@@&lt;VARIABLE&gt;@@</c>), and reads every password as a Key Vault reference
/// of its own, which the entrypoint fills in; each service reads its own connection string,
/// <c>messaging-&lt;module&gt;-connection-string</c>. The admin's connection string is no longer stored, and the Key Vault
/// holds no definitions document, whose Bicep parameter Aspire named after the whole document (BCP024).
/// </summary>
public class AppHostMessagingTests
{
    private const string KeyVault = "secrets";

    private const string Definitions = "BROKER_DEFINITIONS";

    /// <summary>The broker's variable that holds the admin's password, as RabbitMQ's image reads it.</summary>
    private const string AdminPassword = "RABBITMQ_DEFAULT_PASS";

    /// <summary>The broker's variable that holds the admin's user name, as RabbitMQ's image reads it.</summary>
    private const string AdminUser = "RABBITMQ_DEFAULT_USER";

    [Fact]
    public async Task The_broker_keeps_its_data_on_a_volume_with_explicit_credentials()
    {
        await using var builder = await PublishedAppHost.ModelAsync(PublishedAppHost.Staging, TestContext.Current.CancellationToken);

        var broker = builder.Resources.OfType<RabbitMQServerResource>().ShouldHaveSingleItem();
        var volume = broker.Annotations.OfType<ContainerMountAnnotation>().Where(mount => mount.Type == ContainerMountType.Volume).ShouldHaveSingleItem();

        broker.Name.ShouldBe("messaging");
        volume.Target.ShouldBe("/var/lib/rabbitmq");
        volume.Source.ShouldNotBeNullOrWhiteSpace();
        broker.UserNameParameter.ShouldNotBeNull("The broker's user is RabbitMQ's default, not a parameter.").Name.ShouldBe("messaging-username");
        broker.PasswordParameter.Name.ShouldBe("messaging-password");
        broker.PasswordParameter.Default.ShouldBeNull("The broker's password is generated; the deployment must set it.");
        broker.Annotations.OfType<EndpointAnnotation>().ShouldAllBe(endpoint => !endpoint.IsExternal);
    }

    [Fact]
    public async Task The_broker_is_given_its_definitions_as_a_plain_value_without_a_password()
    {
        await using var builder = await PublishedAppHost.ModelAsync(PublishedAppHost.Staging, TestContext.Current.CancellationToken);
        var broker = builder.Resources.OfType<RabbitMQServerResource>().ShouldHaveSingleItem();
        var modules = PublishedAppHost.Services(builder).Select(service => service.Name).ToList();

        var environment = await EnvironmentAsync(builder, broker);

        modules.ShouldNotBeEmpty();
        environment.ShouldContainKey(Definitions, "The broker is given no definitions; it creates no user of a service's own.");
        var definitions = environment[Definitions];
        definitions.ShouldNotBeAssignableTo<IAzureKeyVaultSecretReference>("The broker's definitions are a Key Vault secret again; Aspire names that secret's Bicep parameter after the whole document (BCP024).");
        Secrets(definitions).ShouldBeEmpty("The broker's definitions carry a secret; each password reaches the broker as a Key Vault reference of its own.");
        var text = Text(definitions);
        text.ShouldContain($"\"password\":\"{Placeholder(AdminPassword)}\"", Case.Sensitive, "The admin's password in the definitions is not the placeholder of RABBITMQ_DEFAULT_PASS.");
        foreach (var module in modules)
        {
            text.ShouldContain($"\"password\":\"{Placeholder(PasswordVariable(module))}\"", Case.Sensitive, $"{module}'s password in the definitions is not the placeholder of {PasswordVariable(module)}.");
        }
    }

    [Fact]
    public async Task The_definitions_carry_every_user_name_as_the_placeholder_of_a_broker_variable_holding_it()
    {
        await using var builder = await PublishedAppHost.ModelAsync(PublishedAppHost.Staging, TestContext.Current.CancellationToken);
        var broker = builder.Resources.OfType<RabbitMQServerResource>().ShouldHaveSingleItem();
        var modules = PublishedAppHost.Services(builder).Select(service => service.Name).ToList();

        var environment = await EnvironmentAsync(builder, broker);

        modules.ShouldNotBeEmpty();
        var definitions = environment[Definitions].ShouldBeOfType<string>("The broker's definitions hold a value the AppHost puts in; every user name is a placeholder the entrypoint fills in and checks.");
        definitions.ShouldContain($"{{\"name\":\"{Placeholder(AdminUser)}\",", Case.Sensitive, "The admin's user name in the definitions is not the placeholder of RABBITMQ_DEFAULT_USER.");
        definitions.ShouldContain($"{{\"user\":\"{Placeholder(AdminUser)}\",", Case.Sensitive, "The admin's permissions in the definitions do not name the placeholder of RABBITMQ_DEFAULT_USER.");
        environment.ShouldContainKey(AdminUser);
        foreach (var module in modules)
        {
            var variable = BrokerUsers.UserVariable(module);
            definitions.ShouldContain($"{{\"name\":\"{Placeholder(variable)}\",", Case.Sensitive, $"{module}'s user name in the definitions is not the placeholder of {variable}.");
            definitions.ShouldContain($"{{\"user\":\"{Placeholder(variable)}\",", Case.Sensitive, $"{module}'s permissions in the definitions do not name the placeholder of {variable}.");
            environment.ShouldContainKey(variable, $"The broker is not given {module}'s user name as {variable}.");
            environment[variable].ShouldBe(module);
        }
    }

    [Fact]
    public async Task The_broker_reads_every_users_password_as_a_key_vault_reference_of_its_own()
    {
        await using var builder = await PublishedAppHost.ModelAsync(PublishedAppHost.Staging, TestContext.Current.CancellationToken);
        var broker = builder.Resources.OfType<RabbitMQServerResource>().ShouldHaveSingleItem();
        var modules = PublishedAppHost.Services(builder).Select(service => service.Name).ToList();
        var stored = builder.Resources.OfType<AzureKeyVaultSecretResource>().Select(secret => secret.SecretName).ToList();

        var environment = await EnvironmentAsync(builder, broker);

        modules.ShouldNotBeEmpty();
        foreach (var (variable, secret) in modules.Select(module => (PasswordVariable(module), $"messaging-{module}-password")).Prepend((AdminPassword, "messaging-password")))
        {
            environment.ShouldContainKey(variable, $"The broker is not given {secret} as {variable}.");
            var reference = environment[variable].ShouldBeAssignableTo<IAzureKeyVaultSecretReference>($"The broker reads {variable} as a plain value.");
            reference.ShouldNotBeNull();
            reference.SecretName.ShouldBe(secret);
            reference.Resource.Name.ShouldBe(KeyVault);
            stored.ShouldContain(secret);
        }
    }

    [Fact]
    public async Task The_key_vault_holds_no_definitions_document()
    {
        await using var builder = await PublishedAppHost.ModelAsync(PublishedAppHost.Staging, TestContext.Current.CancellationToken);

        var stored = builder.Resources.OfType<AzureKeyVaultSecretResource>().Select(secret => secret.SecretName).ToList();

        stored.ShouldNotBeEmpty();
        stored.ShouldNotContain("messaging-definitions", "The Key Vault still holds the broker's definitions; they carry no secret and are a plain value.");
    }

    [Fact]
    public async Task The_broker_writes_its_definitions_to_a_file_and_loads_them_at_every_boot()
    {
        await using var builder = await PublishedAppHost.ModelAsync(PublishedAppHost.Staging, TestContext.Current.CancellationToken);
        var broker = builder.Resources.OfType<RabbitMQServerResource>().ShouldHaveSingleItem();

        List<object> arguments = [];
        var context = new CommandLineArgsCallbackContext(arguments, broker, TestContext.Current.CancellationToken);
        foreach (var callback in broker.Annotations.OfType<CommandLineArgsCallbackAnnotation>())
        {
            await callback.Callback(context);
        }

        broker.Entrypoint.ShouldBe("/bin/bash");
        arguments.Count.ShouldBe(2, $"The broker's arguments are {string.Join(" ", arguments)}");
        arguments[0].ShouldBe("-c");
        var script = arguments[1].ShouldBeOfType<string>();
        script.ShouldContain($"${Definitions}");
        script.ShouldContain("@@", Case.Sensitive, "The broker's entrypoint does not fill in the definitions' password placeholders.");
        script.ShouldContain("load_definitions");
        script.ShouldContain("docker-entrypoint.sh rabbitmq-server");
    }

    [Fact]
    public async Task Every_service_reads_a_connection_string_of_its_own_from_the_key_vault()
    {
        await using var builder = await PublishedAppHost.ModelAsync(PublishedAppHost.Staging, TestContext.Current.CancellationToken);
        var services = PublishedAppHost.Services(builder);
        var stored = builder.Resources.OfType<AzureKeyVaultSecretResource>().Select(secret => secret.SecretName).ToList();

        services.ShouldNotBeEmpty();
        foreach (var service in services)
        {
            var environment = await EnvironmentAsync(builder, service);

            environment.ShouldContainKey("ConnectionStrings__messaging", $"{service.Name} reads no broker connection string.");
            var connectionString = environment["ConnectionStrings__messaging"].ShouldBeAssignableTo<IAzureKeyVaultSecretReference>($"{service.Name} reads the broker as a plain value.");
            connectionString.ShouldNotBeNull();
            connectionString.SecretName.ShouldBe($"messaging-{service.Name}-connection-string", $"{service.Name} does not read a broker connection string of its own.");
            connectionString.Resource.Name.ShouldBe(KeyVault);
            stored.ShouldContain($"messaging-{service.Name}-connection-string");
        }
    }

    [Fact]
    public async Task The_admins_connection_string_is_no_longer_stored()
    {
        await using var builder = await PublishedAppHost.ModelAsync(PublishedAppHost.Staging, TestContext.Current.CancellationToken);

        var stored = builder.Resources.OfType<AzureKeyVaultSecretResource>().Select(secret => secret.SecretName).ToList();

        stored.ShouldNotBeEmpty();
        stored.ShouldNotContain("messaging-connection-string", "The Key Vault still holds the broker admin's connection string, which no app may read.");
    }

    /// <summary>The broker's variable that holds a service user's password: <c>BROKER_PASSWORD_&lt;MODULE&gt;</c>.</summary>
    private static string PasswordVariable(string module) => BrokerUsers.PasswordVariable(module);

    /// <summary>The text that stands for a variable's value in the definitions until the entrypoint fills it in.</summary>
    private static string Placeholder(string variable) => BrokerUsers.Placeholder(variable);

    /// <summary>Every secret a value carries, however deeply an expression nests it: Key Vault references and secret parameters.</summary>
    private static List<string> Secrets(object value) => value switch
    {
        IAzureKeyVaultSecretReference reference => [$"the Key Vault secret {reference.SecretName}"],
        ParameterResource { Secret: true } parameter => [$"the secret parameter {parameter.Name}"],
        ReferenceExpression expression => [.. expression.ValueProviders.SelectMany(Secrets)],
        _ => [],
    };

    /// <summary>A value's literal text: the string itself, or an expression's format with its placeholders left in.</summary>
    private static string Text(object value) => value switch
    {
        string text => text,
        ReferenceExpression expression => expression.Format,
        _ => throw new ShouldAssertException($"The broker's definitions are a {value.GetType().Name}, neither a string nor an expression."),
    };

    /// <summary>The resource's environment as the publish model sets it, values unresolved.</summary>
    private static async Task<Dictionary<string, object>> EnvironmentAsync(IDistributedApplicationTestingBuilder builder, IResource resource)
    {
        var context = new EnvironmentCallbackContext(builder.ExecutionContext, resource, cancellationToken: TestContext.Current.CancellationToken);
        foreach (var callback in resource.Annotations.OfType<EnvironmentCallbackAnnotation>())
        {
            await callback.Callback(context);
        }

        return context.EnvironmentVariables;
    }
}
