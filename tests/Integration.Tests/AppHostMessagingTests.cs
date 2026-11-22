using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure;
using Aspire.Hosting.Testing;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// In publish mode RabbitMQ is a container whose data outlives a restart on a named volume, with the user and password the
/// deployment sets, and its management endpoint stays inside the environment. Every service connects as a user of its own
/// (#183): the broker imports the users from the Key Vault secret <c>messaging-definitions</c> at every boot, through an
/// entrypoint of its own, and each service reads its own connection string, <c>messaging-&lt;module&gt;-connection-string</c>;
/// the admin's connection string is no longer stored.
/// </summary>
public class AppHostMessagingTests
{
    private const string KeyVault = "secrets";

    private const string Definitions = "BROKER_DEFINITIONS";

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
    public async Task The_broker_reads_its_users_definitions_as_a_key_vault_reference()
    {
        await using var builder = await PublishedAppHost.ModelAsync(PublishedAppHost.Staging, TestContext.Current.CancellationToken);
        var broker = builder.Resources.OfType<RabbitMQServerResource>().ShouldHaveSingleItem();

        var environment = await EnvironmentAsync(builder, broker);

        environment.ShouldContainKey(Definitions, "The broker is given no definitions; it creates no user of a service's own.");
        var definitions = environment[Definitions].ShouldBeAssignableTo<IAzureKeyVaultSecretReference>("The broker reads its definitions, which hold every service's password, as a plain value.");
        definitions.ShouldNotBeNull();
        definitions.SecretName.ShouldBe("messaging-definitions");
        definitions.Resource.Name.ShouldBe(KeyVault);
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
        script.ShouldContain("load_definitions");
        script.ShouldContain($"unset {Definitions}");
        script.ShouldContain("exec docker-entrypoint.sh rabbitmq-server");
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

        stored.ShouldNotContain("messaging-connection-string", "The Key Vault still holds the broker admin's connection string, which no app may read.");
        stored.ShouldContain("messaging-definitions");
    }

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
