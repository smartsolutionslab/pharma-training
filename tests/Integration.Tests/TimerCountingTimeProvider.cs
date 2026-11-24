using Microsoft.Extensions.Time.Testing;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// A <see cref="FakeTimeProvider"/> that counts the timers made on it, so a test advances the clock only once the code it
/// drives is waiting on one; an advance before the timer exists would never fire it.
/// </summary>
internal sealed class TimerCountingTimeProvider : FakeTimeProvider
{
    private int timers;

    /// <summary>How many timers have been made on it.</summary>
    public int Timers => Volatile.Read(ref timers);

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = base.CreateTimer(callback, state, dueTime, period);
        Interlocked.Increment(ref timers);
        return timer;
    }
}
