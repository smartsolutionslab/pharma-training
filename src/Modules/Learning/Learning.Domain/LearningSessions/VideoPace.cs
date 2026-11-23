namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.LearningSessions;

/// <summary>
/// How fast video progress may grow (ADR 0024): a gain is plausible exactly when watching it at 1.25 times speed fits
/// into the time elapsed, <c>(r - p) × d × 8 ≤ e</c> with the points gained, the duration in seconds and the elapsed
/// milliseconds. The tolerance is a factor on the elapsed time, never an additive slack, which every report would
/// grant again.
/// </summary>
public static class VideoPace
{
    // One point of a video of d seconds takes d × 10 ms at normal speed; at 1.25 times speed, d × 8 ms.
    private const long MillisecondsPerPointAndSecond = 8;

    public static bool Allows(ProgressPercent from, ProgressPercent to, VideoDuration duration, Timestamp since, Timestamp now)
    {
        Ensure.That(from).IsNotNull();
        Ensure.That(to).IsNotNull();
        Ensure.That(duration).IsNotNull();
        Ensure.That(since).IsNotNull();
        Ensure.That(now).IsNotNull();

        // Two replicas' clocks apart: time running backwards counts as none.
        var elapsedMilliseconds = Math.Max(0L, (now.Value - since.Value).Ticks / TimeSpan.TicksPerMillisecond);
        var requiredMilliseconds = (long)(to.Value - from.Value) * duration.Value * MillisecondsPerPointAndSecond;
        return requiredMilliseconds <= elapsedMilliseconds;
    }
}
