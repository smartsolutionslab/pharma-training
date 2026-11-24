using System.Collections.Concurrent;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.RateLimits;

/// <summary>
/// Counts the fixed windows of every <see cref="UserRateLimit"/> per caller, in this process's memory, on the
/// <see cref="TimeProvider"/> (docs/adr/0020). A request is admitted and counted in every window of its limit, or
/// refused and counted in none; each window starts with the caller's first admitted request after the previous one
/// ended. One singleton per host. A counter whose windows have all ended counts nothing any more and is forgotten, at
/// most once every <see cref="SweepInterval"/>, by the request that finds the sweep due (#292): memory then holds the
/// callers of the longest window, not every caller since the host started, which a client rotating through IPv6 /64s
/// would otherwise grow without end (docs/adr/0030).
/// </summary>
public sealed class UserRateLimiter
{
    /// <summary>How often the counters are swept for those whose windows have all ended.</summary>
    public static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(1);

    private readonly TimeProvider time;
    private readonly ConcurrentDictionary<(string Limit, string Subject), Counter> counters = new();
    private readonly Lock sweeping = new();
    // When the next sweep is due, as UTC ticks so a request can read it without the lock.
    private long nextSweepTicks;

    public UserRateLimiter(TimeProvider time)
    {
        Ensure.That(time).IsNotNull();
        this.time = time;
    }

    /// <summary>How many callers, under all limits, the limiter holds a counter for.</summary>
    public int TrackedCallers => counters.Count;

    /// <summary>Admits and counts the caller's request under <paramref name="limit"/>, or refuses it uncounted.</summary>
    /// <param name="limit">The limit the endpoint names.</param>
    /// <param name="subject">The caller (<see cref="UserRateSubject"/>); empty for a principal without one.</param>
    public UserRateDecision TryAcquire(UserRateLimit limit, string subject)
    {
        Ensure.That(limit).IsNotNull();
        Ensure.That(subject).IsNotNull();

        var now = time.GetUtcNow();
        SweepIfDue(now);

        // A counter a sweep forgot between finding it and locking it is retired: count in the one that replaced it.
        while (true)
        {
            var counter = counters.GetOrAdd((limit.Name, subject), _ => new Counter(limit.Windows));
            lock (counter)
            {
                if (!counter.IsRetired)
                {
                    return counter.TryAcquire(limit.Windows, now);
                }
            }
        }
    }

    /// <summary>
    /// Forgets the counters whose windows have all ended, when a sweep is due and no other request is sweeping. Each
    /// counter is retired under its own lock before it is removed, so a request that already holds it counts again.
    /// </summary>
    private void SweepIfDue(DateTimeOffset now)
    {
        if (now.UtcTicks < Volatile.Read(ref nextSweepTicks) || !sweeping.TryEnter())
        {
            return;
        }

        try
        {
            if (now.UtcTicks < nextSweepTicks)
            {
                return;
            }

            Volatile.Write(ref nextSweepTicks, (now + SweepInterval).UtcTicks);
            foreach (var entry in counters)
            {
                lock (entry.Value)
                {
                    if (entry.Value.HasEnded(now))
                    {
                        entry.Value.Retire();
                        counters.TryRemove(entry);
                    }
                }
            }
        }
        finally
        {
            sweeping.Exit();
        }
    }

    /// <summary>
    /// One caller's windows under one limit: when each began and how many it admitted, until a sweep retires it. Guarded
    /// by its own lock.
    /// </summary>
    private sealed class Counter
    {
        private readonly DateTimeOffset[] starts;
        private readonly int[] counts;
        private readonly TimeSpan[] lengths;

        public Counter(IReadOnlyList<UserRateWindow> windows)
        {
            starts = new DateTimeOffset[windows.Count];
            counts = new int[windows.Count];
            lengths = [.. windows.Select(window => window.Length)];
        }

        public bool IsRetired { get; private set; }

        public void Retire() => IsRetired = true;

        /// <summary>Whether every window has ended, so the counter would start each afresh: it counts nothing any more.</summary>
        public bool HasEnded(DateTimeOffset now)
        {
            for (var index = 0; index < starts.Length; index++)
            {
                if (starts[index] + lengths[index] > now)
                {
                    return false;
                }
            }

            return true;
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
