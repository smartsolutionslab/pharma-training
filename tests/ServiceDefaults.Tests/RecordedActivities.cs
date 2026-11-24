using System.Collections.Concurrent;
using System.Diagnostics;

namespace SmartSolutionsLab.PharmaTraining.ServiceDefaults.Tests;

/// <summary>Every activity of every source that stops while this is undisposed, with all its data, as an exporter sees it.</summary>
public sealed class RecordedActivities : IDisposable
{
    private readonly ConcurrentQueue<Activity> stopped = [];

    private readonly ActivityListener listener;

    public RecordedActivities()
    {
        listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = stopped.Enqueue,
        };
        ActivitySource.AddActivityListener(listener);
    }

    public IReadOnlyList<Activity> Stopped => [.. stopped];

    public void Dispose() => listener.Dispose();
}
