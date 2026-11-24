namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.RateLimits;

/// <summary>What <see cref="UserRateLimiter.TryAcquire"/> decided: admitted, or refused for <see cref="RetryAfter"/>.</summary>
public sealed class UserRateDecision
{
    private UserRateDecision(bool isAdmitted, TimeSpan retryAfter)
    {
        IsAdmitted = isAdmitted;
        RetryAfter = retryAfter;
    }

    public static UserRateDecision Admitted { get; } = new(true, TimeSpan.Zero);

    public bool IsAdmitted { get; }

    /// <summary>How long until the latest-ending full window ends; <see cref="TimeSpan.Zero"/> when admitted.</summary>
    public TimeSpan RetryAfter { get; }

    public static UserRateDecision Refused(TimeSpan retryAfter)
    {
        Ensure.That(retryAfter.CompareTo(TimeSpan.Zero), nameof(retryAfter)).Satisfies(sign => sign > 0, $"a refusal waits some time; got {retryAfter}.");
        return new UserRateDecision(false, retryAfter);
    }
}
