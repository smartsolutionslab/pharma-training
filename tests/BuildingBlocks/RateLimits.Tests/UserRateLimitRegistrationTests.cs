using Microsoft.Extensions.DependencyInjection;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.RateLimits.Tests;

/// <summary>
/// A module registers each of its limits by name in its AddServices; the names share one registry and one limiter per
/// service collection, and a name registered twice stops the host at startup, naming it.
/// </summary>
public class UserRateLimitRegistrationTests
{
    [Fact]
    public void A_registered_limit_is_found_by_its_name()
    {
        var services = new ServiceCollection();
        var limit = Limit("test.ask");

        services.AddUserRateLimit(limit);

        using var provider = services.BuildServiceProvider();
        var found = provider.GetRequiredService<UserRateLimits>().Find("test.ask");
        found.HasValue.ShouldBeTrue();
        found.Value.ShouldBeSameAs(limit);
        provider.GetRequiredService<UserRateLimits>().Find("test.other").HasValue.ShouldBeFalse();
    }

    [Fact]
    public void A_name_registered_twice_stops_the_host_naming_it()
    {
        var services = new ServiceCollection();
        services.AddUserRateLimit(Limit("test.ask"));

        var again = () => services.AddUserRateLimit(Limit("test.ask"));

        Should.Throw<InvalidOperationException>(again).Message.ShouldContain("test.ask");
    }

    [Fact]
    public void The_limiter_is_registered_once_for_several_limits()
    {
        var services = new ServiceCollection();

        services.AddUserRateLimit(Limit("test.ask"));
        services.AddUserRateLimit(Limit("test.draft"));

        services.Count(service => service.ServiceType == typeof(UserRateLimiter)).ShouldBe(1);
        services.Count(service => service.ServiceType == typeof(UserRateLimits)).ShouldBe(1);
        using var provider = services.BuildServiceProvider();
        var limits = provider.GetRequiredService<UserRateLimits>();
        limits.Find("test.ask").HasValue.ShouldBeTrue();
        limits.Find("test.draft").HasValue.ShouldBeTrue();
    }

    private static UserRateLimit Limit(string name) =>
        UserRateLimit.Of(name, new TestLimitReached(), UserRateWindow.Of(2, TimeSpan.FromMinutes(1)));
}
