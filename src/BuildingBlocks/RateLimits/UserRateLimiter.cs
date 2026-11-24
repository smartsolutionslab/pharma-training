using System.Collections.Concurrent;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.RateLimits;

/// <summary>
/// Counts the fixed windows of every <see cref="UserRateLimit"/> per caller, in this process's memory, on the
/// <see cref="TimeProvider"/> (docs/adr/0020). A request is admitted and counted in every window of its limit, or
/// refused and counted in none; each window starts with the caller's first admitted request after the previous one
/// ended. One singleton per host.
/// </summary>
public sealed class UserRateLimiter
{
    private readonly TimeProvider time;
    private readonly ConcurrentDictionary<(string Limit, string Subject), Counter> counters = new();

    public UserRateLimiter(TimeProvider time)
    {
        Ensure.That(time).IsNotNull();
        this.time = time;
    }

    /// <summary>Admits and counts the caller's request under <paramref name="limit"/>, or refuses it uncounted.</summary>
    /// <param name="limit">The limit the endpoint names.</param>
    /// <param name="subject">The caller (<see cref="UserRateSubject"/>); empty for a principal without one.</param>
    public UserRateDecision TryAcquire(UserRateLimit limit, string subject)
    {
        Ensure.That(limit).IsNotNull();
        Ensure.That(subject).IsNotNull();

        var counter = counters.GetOrAdd((limit.Name, subject), _ => new Counter(limit.Windows.Count));
        lock (counter)
        {
            return counter.TryAcquire(limit.Windows, time.GetUtcNow());
        }
    }

    /// <summary>One caller's windows under one limit: when each began and how many it admitted. Guarded by its own lock.</summary>
    private sealed class Counter
    {
        private readonly DateTimeOffset[] starts;
        private readonly int[] counts;

        public Counter(int windows)
        {
            starts = new DateTimeOffset[windows];
            counts = new int[windows];
        }

        public UserRateDecision TryAcquire(IReadOnlyList<UserRateWindow> windows, DateTimeOffset now)
        {
            var wait = TimeSpan.Zero;
            for (var index = 0; index < windows.Count; index++)
            {
                var ends = starts[index] + windows[index].Length;
                if (ends > now && counts[index] >= windows[index].Permits && ends - now > wait)
                {
                    wait = ends - now;
                }
            }

            if (wait > TimeSpan.Zero)
            {
                return UserRateDecision.Refused(wait);
            }

            for (var index = 0; index < windows.Count; index++)
            {
                if (starts[index] + windows[index].Length <= now)
                {
                    starts[index] = now;
                    counts[index] = 0;
                }

                counts[index]++;
            }

            return UserRateDecision.Admitted;
        }
    }
}
