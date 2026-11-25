using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Time.Testing;

namespace SmartSolutionsLab.PharmaTraining.DemoSeeder.Tests;

/// <summary>
/// Runs the seeder against the fake application on a fake clock. The clock starts at 23:30 UTC on 10 March 2026, which is
/// already 11 March in Germany, and moves a second at a time while a run is in progress, so a run that waits on its
/// <see cref="TimeProvider"/> goes on; a run that waits on the wall clock instead is stopped after a minute.
/// </summary>
internal sealed class SeederHarness
{
    public static readonly DateTimeOffset Start = new(2026, 3, 10, 23, 30, 0, TimeSpan.Zero);

    private static readonly TimeSpan WallClockLimit = TimeSpan.FromMinutes(1);

    public SeederHarness()
    {
        App = new FakeApplication(Time);
    }

    public FakeTimeProvider Time { get; } = new(Start);

    public FakeApplication App { get; }

    public StringWriter Output { get; } = new();

    /// <summary>One run to its end; answers the exit code.</summary>
    public async Task<int> RunAsync(SeedPlan plan, IConfiguration? configuration = null)
    {
        var started = TimeProvider.System.GetTimestamp();
        var run = SeederRun.RunAsync(configuration ?? LocalSettings.Build(), plan, App, Time, Output, TestContext.Current.CancellationToken);
        while (!run.IsCompleted)
        {
            if (TimeProvider.System.GetElapsedTime(started) > WallClockLimit)
            {
                throw new TimeoutException($"The run did not end within {WallClockLimit} of the wall clock. Output:\n{Output}");
            }

            Time.Advance(TimeSpan.FromSeconds(1));
            await Task.Delay(TimeSpan.FromMilliseconds(1), TestContext.Current.CancellationToken);
        }

        return await run;
    }

    /// <summary>One run that must succeed; fails the test with its output otherwise.</summary>
    public async Task SeedAsync(SeedPlan plan)
    {
        var exitCode = await RunAsync(plan);
        exitCode.ShouldBe(0, $"The run failed. Output:\n{Output}\nRequests:\n{string.Join('\n', App.Requests)}");
    }
}
