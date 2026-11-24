namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.RateLimits.Tests;

/// <summary>A window is a number of permits over a length of time; one that admits nothing or lasts no time is refused.</summary>
public class UserRateWindowTests
{
    [Fact]
    public void A_window_keeps_its_permits_and_length()
    {
        var window = UserRateWindow.Of(10, TimeSpan.FromMinutes(1));

        window.Permits.ShouldBe(10);
        window.Length.ShouldBe(TimeSpan.FromMinutes(1));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_window_of_no_permits_is_refused(int permits)
    {
        Should.Throw<GuardException>(() => UserRateWindow.Of(permits, TimeSpan.FromMinutes(1)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_window_of_no_length_is_refused(int seconds)
    {
        Should.Throw<GuardException>(() => UserRateWindow.Of(1, TimeSpan.FromSeconds(seconds)));
    }
}
