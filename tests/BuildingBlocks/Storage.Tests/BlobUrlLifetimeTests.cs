namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Storage.Tests;

public class BlobUrlLifetimeTests
{
    [Fact]
    public void Fifteen_minutes_is_the_maximum()
    {
        BlobUrlLifetime.Maximum.ShouldBe(TimeSpan.FromMinutes(15));
        BlobUrlLifetime.From(TimeSpan.FromMinutes(15)).Value.ShouldBe(TimeSpan.FromMinutes(15));
        BlobUrlLifetime.From(TimeSpan.FromSeconds(1)).Value.ShouldBe(TimeSpan.FromSeconds(1));
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-TimeSpan.TicksPerSecond)]
    [InlineData((15 * TimeSpan.TicksPerMinute) + 1)]
    public void A_lifetime_outside_zero_to_fifteen_minutes_is_refused(long ticks) =>
        Should.Throw<GuardException>(() => BlobUrlLifetime.From(TimeSpan.FromTicks(ticks)));
}
