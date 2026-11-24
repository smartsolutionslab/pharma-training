using Microsoft.Extensions.Time.Testing;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.RateLimits.Tests;

/// <summary>
/// The limiter counts fixed windows per limit and caller on <see cref="TimeProvider"/>: each window starts with the
/// caller's first admitted request after the previous one ended. A request is admitted and counted in every window, or
/// refused and counted in none; a refusal says how long until the latest-ending full window ends.
/// </summary>
public class UserRateLimiterTests
{
    private const string Caller = "0198c1a0-0000-7000-8000-000000000001";
    private const string Other = "0198c1a0-0000-7000-8000-000000000002";

    private readonly FakeTimeProvider time = new(new DateTimeOffset(2026, 11, 23, 9, 0, 0, TimeSpan.Zero));

    [Fact]
    public void A_caller_is_admitted_up_to_the_windows_permits()
    {
        var limiter = new UserRateLimiter(time);
        var limit = Limit("test.ask", UserRateWindow.Of(3, TimeSpan.FromMinutes(1)));

        for (var request = 1; request <= 3; request++)
        {
            var decision = limiter.TryAcquire(limit, Caller);

            decision.IsAdmitted.ShouldBeTrue($"request {request} of 3");
            decision.RetryAfter.ShouldBe(TimeSpan.Zero);
        }
    }

    [Fact]
    public void The_request_after_the_last_permit_is_refused_until_the_window_ends()
    {
        var limiter = new UserRateLimiter(time);
        var limit = Limit("test.ask", UserRateWindow.Of(2, TimeSpan.FromMinutes(1)));
        limiter.TryAcquire(limit, Caller).IsAdmitted.ShouldBeTrue();
        limiter.TryAcquire(limit, Caller).IsAdmitted.ShouldBeTrue();
        time.Advance(TimeSpan.FromSeconds(20));

        var refused = limiter.TryAcquire(limit, Caller);

        refused.IsAdmitted.ShouldBeFalse();
        refused.RetryAfter.ShouldBe(TimeSpan.FromSeconds(40));
    }

    [Fact]
    public void A_refused_request_counts_against_no_window()
    {
        var limiter = new UserRateLimiter(time);
        var limit = Limit("test.ask", UserRateWindow.Of(2, TimeSpan.FromMinutes(1)), UserRateWindow.Of(3, TimeSpan.FromDays(1)));
        limiter.TryAcquire(limit, Caller).IsAdmitted.ShouldBeTrue();
        limiter.TryAcquire(limit, Caller).IsAdmitted.ShouldBeTrue();
        limiter.TryAcquire(limit, Caller).IsAdmitted.ShouldBeFalse("the minute is full");
        time.Advance(TimeSpan.FromMinutes(1));

        limiter.TryAcquire(limit, Caller).IsAdmitted.ShouldBeTrue("the refused request did not count against the day");
        limiter.TryAcquire(limit, Caller).IsAdmitted.ShouldBeFalse("the day has had its 3");
    }

    [Fact]
    public void The_window_admits_again_once_it_has_ended()
    {
        var limiter = new UserRateLimiter(time);
        var limit = Limit("test.ask", UserRateWindow.Of(1, TimeSpan.FromMinutes(1)));
        limiter.TryAcquire(limit, Caller).IsAdmitted.ShouldBeTrue();
        time.Advance(TimeSpan.FromSeconds(59));
        limiter.TryAcquire(limit, Caller).IsAdmitted.ShouldBeFalse("a second of the window is left");
        time.Advance(TimeSpan.FromSeconds(1));

        var admitted = limiter.TryAcquire(limit, Caller);

        admitted.IsAdmitted.ShouldBeTrue();
        limiter.TryAcquire(limit, Caller).IsAdmitted.ShouldBeFalse("a new window began with the admitted request");
    }

    [Fact]
    public void The_day_window_refuses_after_the_minute_windows_have_passed()
    {
        var limiter = new UserRateLimiter(time);
        var limit = Limit("test.ask", UserRateWindow.Of(2, TimeSpan.FromMinutes(1)), UserRateWindow.Of(4, TimeSpan.FromDays(1)));
        for (var minute = 0; minute < 2; minute++)
        {
            limiter.TryAcquire(limit, Caller).IsAdmitted.ShouldBeTrue();
            limiter.TryAcquire(limit, Caller).IsAdmitted.ShouldBeTrue();
            time.Advance(TimeSpan.FromMinutes(1));
        }

        var refused = limiter.TryAcquire(limit, Caller);

        refused.IsAdmitted.ShouldBeFalse();
        refused.RetryAfter.ShouldBe(TimeSpan.FromDays(1) - TimeSpan.FromMinutes(2));
    }

    [Fact]
    public void Retry_after_is_the_longest_wait_of_the_full_windows()
    {
        var limiter = new UserRateLimiter(time);
        var limit = Limit("test.ask", UserRateWindow.Of(2, TimeSpan.FromMinutes(1)), UserRateWindow.Of(2, TimeSpan.FromHours(1)));
        limiter.TryAcquire(limit, Caller).IsAdmitted.ShouldBeTrue();
        limiter.TryAcquire(limit, Caller).IsAdmitted.ShouldBeTrue();
        time.Advance(TimeSpan.FromSeconds(10));

        var refused = limiter.TryAcquire(limit, Caller);

        refused.IsAdmitted.ShouldBeFalse();
        refused.RetryAfter.ShouldBe(TimeSpan.FromHours(1) - TimeSpan.FromSeconds(10), "both windows are full; the hour ends last");
    }

    [Fact]
    public void One_callers_requests_do_not_count_against_another()
    {
        var limiter = new UserRateLimiter(time);
        var limit = Limit("test.ask", UserRateWindow.Of(1, TimeSpan.FromMinutes(1)));
        limiter.TryAcquire(limit, Caller).IsAdmitted.ShouldBeTrue();
        limiter.TryAcquire(limit, Caller).IsAdmitted.ShouldBeFalse();

        limiter.TryAcquire(limit, Other).IsAdmitted.ShouldBeTrue();
    }

    [Fact]
    public void Two_limits_count_apart_for_one_caller()
    {
        var limiter = new UserRateLimiter(time);
        var ask = Limit("test.ask", UserRateWindow.Of(1, TimeSpan.FromMinutes(1)));
        var draft = Limit("test.draft", UserRateWindow.Of(1, TimeSpan.FromMinutes(1)));
        limiter.TryAcquire(ask, Caller).IsAdmitted.ShouldBeTrue();
        limiter.TryAcquire(ask, Caller).IsAdmitted.ShouldBeFalse();

        limiter.TryAcquire(draft, Caller).IsAdmitted.ShouldBeTrue();
    }

    [Fact]
    public void Callers_without_a_subject_share_one_count()
    {
        var limiter = new UserRateLimiter(time);
        var limit = Limit("test.ask", UserRateWindow.Of(1, TimeSpan.FromMinutes(1)));
        limiter.TryAcquire(limit, string.Empty).IsAdmitted.ShouldBeTrue();

        limiter.TryAcquire(limit, string.Empty).IsAdmitted.ShouldBeFalse("a second caller without a subject shares the count");
    }

    [Fact]
    public async Task Concurrent_requests_admit_exactly_the_permits()
    {
        var limiter = new UserRateLimiter(time);
        var limit = Limit("test.ask", UserRateWindow.Of(10, TimeSpan.FromMinutes(1)), UserRateWindow.Of(50, TimeSpan.FromDays(1)));
        var admitted = 0;

        await Parallel.ForAsync(0, 200, TestContext.Current.CancellationToken, (_, _) =>
        {
            if (limiter.TryAcquire(limit, Caller).IsAdmitted)
            {
                Interlocked.Increment(ref admitted);
            }

            return ValueTask.CompletedTask;
        });

        admitted.ShouldBe(10);
    }

    /// <summary>
    /// #292: once callers have real addresses, a client rotating through IPv6 /64s would add a counter per prefix for good.
    /// A counter whose windows have all ended counts nothing any more, so the limiter forgets it.
    /// </summary>
    [Fact]
    public void Counters_whose_windows_have_all_ended_are_forgotten()
    {
        var limiter = new UserRateLimiter(time);
        var limit = Limit("test.verify", UserRateWindow.Of(30, TimeSpan.FromMinutes(1)), UserRateWindow.Of(500, TimeSpan.FromDays(1)));
        for (var prefix = 0; prefix < 1000; prefix++)
        {
            limiter.TryAcquire(limit, $"address:2001:db8:{prefix:x}::/64").IsAdmitted.ShouldBeTrue();
        }

        limiter.TrackedCallers.ShouldBe(1000);
        time.Advance(TimeSpan.FromDays(1) + TimeSpan.FromSeconds(1));

        limiter.TryAcquire(limit, "address:203.0.113.9").IsAdmitted.ShouldBeTrue();

        limiter.TrackedCallers.ShouldBe(1, "Every counter but the new caller's had all its windows ended.");
    }

    [Fact]
    public void A_counter_with_a_window_still_running_is_kept_with_its_count()
    {
        var limiter = new UserRateLimiter(time);
        var limit = Limit("test.verify", UserRateWindow.Of(30, TimeSpan.FromMinutes(1)), UserRateWindow.Of(1, TimeSpan.FromDays(1)));
        limiter.TryAcquire(limit, Caller).IsAdmitted.ShouldBeTrue();
        time.Advance(TimeSpan.FromHours(1));

        limiter.TryAcquire(limit, Other).IsAdmitted.ShouldBeTrue();
        var again = limiter.TryAcquire(limit, Caller);

        again.IsAdmitted.ShouldBeFalse("The caller's day window is still running; forgetting it would hand the caller a fresh day.");
        again.RetryAfter.ShouldBe(TimeSpan.FromHours(23));
        limiter.TrackedCallers.ShouldBe(2);
    }

    [Fact]
    public void A_forgotten_caller_starts_afresh()
    {
        var limiter = new UserRateLimiter(time);
        var limit = Limit("test.verify", UserRateWindow.Of(1, TimeSpan.FromMinutes(1)));
        limiter.TryAcquire(limit, Caller).IsAdmitted.ShouldBeTrue();
        time.Advance(TimeSpan.FromMinutes(2));
        limiter.TryAcquire(limit, Other).IsAdmitted.ShouldBeTrue();
        limiter.TrackedCallers.ShouldBe(1);

        limiter.TryAcquire(limit, Caller).IsAdmitted.ShouldBeTrue();
        limiter.TryAcquire(limit, Caller).IsAdmitted.ShouldBeFalse("A caller counted afresh is still limited.");
    }

    [Fact]
    public async Task Callers_counted_while_others_are_forgotten_are_never_counted_twice_or_lost()
    {
        var limiter = new UserRateLimiter(time);
        var limit = Limit("test.verify", UserRateWindow.Of(1000, TimeSpan.FromMinutes(1)));
        for (var caller = 0; caller < 200; caller++)
        {
            limiter.TryAcquire(limit, $"old-{caller}");
        }

        time.Advance(TimeSpan.FromMinutes(2));
        var admitted = 0;

        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
        {
            for (var request = 0; request < 250; request++)
            {
                if (limiter.TryAcquire(limit, Caller).IsAdmitted)
                {
                    Interlocked.Increment(ref admitted);
                }
            }
        }, TestContext.Current.CancellationToken)));

        admitted.ShouldBe(1000, "Exactly the window's permits are admitted, however the forgetting interleaves.");
        limiter.TrackedCallers.ShouldBe(1);
    }

    private static UserRateLimit Limit(string name, params UserRateWindow[] windows) =>
        UserRateLimit.Of(name, new TestLimitReached(), windows);
}
