using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace SmartSolutionsLab.PharmaTraining.Host.Tests;

/// <summary>
/// The publisher section is checked before the host starts (docs/adr/0011, D5): a module the AppHost names no broker user
/// for, or one user named for two modules, would leave a module's events refused as forged, or accepted from the wrong
/// service, at run time. Either stops the host at start, naming what is wrong.
/// </summary>
public class MessagingStartupTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_publisher_stops_the_host_at_start(string blank)
    {
        var configuration = ConfigurationWithEditorial(blank);

        Should.Throw<InvalidOperationException>(() => new ServiceCollection().AddMessaging(configuration, []))
            .Message.ShouldBe("Messaging:Publishers:editorial names no broker user; the AppHost names every module's.");
    }

    [Fact]
    public void A_broker_user_named_for_two_modules_stops_the_host_at_start()
    {
        var configuration = ConfigurationWithEditorial("organisation");

        Should.Throw<InvalidOperationException>(() => new ServiceCollection().AddMessaging(configuration, []))
            .Message.ShouldBe("The broker user 'organisation' is named for the modules editorial, organisation; each module needs a user of its own.");
    }

    /// <summary>A service's messaging settings, with no password anywhere, and the given user for Editorial.</summary>
    private static IConfiguration ConfigurationWithEditorial(string editorial) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["ConnectionStrings:database"] = "Host=localhost;Database=compliance",
                ["ConnectionStrings:messaging"] = "amqp://compliance@localhost:5672",
                ["Messaging:Queue"] = "compliance",
                ["Messaging:Publishers:organisation"] = "organisation",
                ["Messaging:Publishers:editorial"] = editorial,
            })
            .Build();
}
