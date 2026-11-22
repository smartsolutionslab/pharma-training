using Aspire.Hosting.ApplicationModel;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// In publish mode RabbitMQ is a container whose data outlives a restart on a named volume, with the user and password the
/// deployment sets, and its management endpoint stays inside the environment.
/// </summary>
public class AppHostMessagingTests
{
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
}
