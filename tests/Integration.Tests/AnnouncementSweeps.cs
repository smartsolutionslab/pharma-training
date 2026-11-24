namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// Waits for what Learning's or Assessment's announcement sweep (#277) brings about: a stored event the sweep announces
/// only once it is older than the run's grace period, on its next pass, and the announcement then still has to be
/// delivered and handled. So the wait is the grace period and one interval on top of the 30 seconds every other barrier
/// of the run allows a delivery, polled, never slept through.
/// </summary>
internal static class AnnouncementSweeps
{
    public static readonly TimeSpan WaitTimeout = AspireFixture.AnnouncementGracePeriod + AspireFixture.AnnouncementSweepInterval + TimeSpan.FromSeconds(30);

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    /// <summary>Polls the condition until it holds, and fails naming <paramref name="what"/> once <see cref="WaitTimeout"/> has passed.</summary>
    public static async Task WaitUntilAsync(Func<Task<bool>> condition, string what, CancellationToken cancellationToken)
    {
        Ensure.That(condition).IsNotNull();

        var started = TimeProvider.System.GetTimestamp();
        while (!await condition())
        {
            TimeProvider.System.GetElapsedTime(started).ShouldBeLessThan(WaitTimeout, $"{what} within {WaitTimeout.TotalSeconds} s.");
            await Task.Delay(PollInterval, cancellationToken);
        }
    }
}
