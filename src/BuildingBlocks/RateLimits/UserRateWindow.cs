namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.RateLimits;

/// <summary>
/// A fixed window of a <see cref="UserRateLimit"/>: at most <see cref="Permits"/> admitted requests per caller over
/// <see cref="Length"/>, counted from the caller's first admitted request after the previous window ended
/// (docs/adr/0020).
/// </summary>
public sealed class UserRateWindow
{
    private UserRateWindow(int permits, TimeSpan length)
    {
        Permits = permits;
        Length = length;
    }

    public int Permits { get; }

    public TimeSpan Length { get; }

    public static UserRateWindow Of(int permits, TimeSpan length)
    {
        Ensure.That(permits).AtLeast(1);
        Ensure.That(length.CompareTo(TimeSpan.Zero), nameof(length)).Satisfies(sign => sign > 0, $"a window lasts some time; got {length}.");
        return new UserRateWindow(permits, length);
    }
}
