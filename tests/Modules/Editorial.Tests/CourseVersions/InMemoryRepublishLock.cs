using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions;

/// <summary>The republish's lock in memory: held by one run at a time, released when that run disposes it.</summary>
internal sealed class InMemoryRepublishLock : IRepublishLock
{
    public bool Held { get; private set; }

    /// <summary>How often a run took the lock.</summary>
    public int Taken { get; private set; }

    /// <summary>Holds the lock as another run would, until the answer is disposed.</summary>
    public IAsyncDisposable HoldAsAnotherRun()
    {
        Held.ShouldBeFalse();
        Held = true;
        return new Release(this);
    }

    public Task<Option<IAsyncDisposable>> TryTakeAsync(CancellationToken cancellationToken)
    {
        if (Held)
        {
            return Task.FromResult(Option<IAsyncDisposable>.None);
        }

        Held = true;
        Taken++;
        return Task.FromResult(Option<IAsyncDisposable>.Some(new Release(this)));
    }

    private sealed class Release(InMemoryRepublishLock owner) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            owner.Held = false;
            return ValueTask.CompletedTask;
        }
    }
}
