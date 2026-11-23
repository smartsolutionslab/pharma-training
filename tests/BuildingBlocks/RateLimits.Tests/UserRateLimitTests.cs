using System.Net;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.RateLimits.Tests;

/// <summary>A limit has a name, a refusal at 429 and at least one window; anything less is refused when it is made.</summary>
public class UserRateLimitTests
{
    private static readonly UserRateWindow Minute = UserRateWindow.Of(10, TimeSpan.FromMinutes(1));
    private static readonly UserRateWindow Day = UserRateWindow.Of(200, TimeSpan.FromDays(1));

    [Fact]
    public void A_limit_keeps_its_name_refusal_and_windows()
    {
        var refusal = new TestLimitReached();

        var limit = UserRateLimit.Of("test.ask", refusal, Minute, Day);

        limit.Name.ShouldBe("test.ask");
        limit.Refusal.ShouldBe(refusal);
        limit.Windows.ShouldBe([Minute, Day]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_name_is_refused(string name)
    {
        Should.Throw<GuardException>(() => UserRateLimit.Of(name, new TestLimitReached(), Minute));
    }

    [Fact]
    public void A_refusal_other_than_429_is_refused()
    {
        Should.Throw<GuardException>(() => UserRateLimit.Of("test.ask", new NotTooMany(), Minute));
    }

    [Fact]
    public void A_limit_without_windows_is_refused()
    {
        Should.Throw<GuardException>(() => UserRateLimit.Of("test.ask", new TestLimitReached()));
    }

    private sealed record NotTooMany() : ApiError("test.not-too-many", "Refused.", HttpStatusCode.Forbidden);
}
