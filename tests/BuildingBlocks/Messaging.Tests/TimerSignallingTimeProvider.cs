using Microsoft.Extensions.Time.Testing;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging.Tests;

/// <summary>
/// A <see cref="FakeTimeProvider"/> that says when a timer was created on it. A timer counts from the fake time it was
/// created at, so a test that drives a background loop waits for its timer before it advances the clock; otherwise the
/// advance may happen first and the timer never fire.
/// </summary>
internal sealed class TimerSignallingTimeProvider(DateTimeOffset start) : FakeTimeProvider(start)
{
    private readonly TaskCompletionSource timerCreated = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Completes once the first timer is created.</summary>
    public Task TimerCreated => timerCreated.Task;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = base.CreateTimer(callback, state, dueTime, period);
        timerCreated.TrySetResult();
        return timer;
    }
}
