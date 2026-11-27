using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging.Tests;

/// <summary>
/// A module's sweep of erased persons' messages (#439) asks the purge, every interval of its settings and starting one
/// interval after start, to delete the dead letters and handled envelopes of the module's personal message types whose
/// body holds an erased person's id in the module's schema. A pass that removed rows is logged at Information with the
/// count and the module only; one that removed nothing logs nothing; one that throws is logged as an error and the next
/// interval runs again; stopping ends the loop quietly. The clock is a <see cref="TimerSignallingTimeProvider"/>, moved
/// by hand; each pass completes a task the test awaits.
/// </summary>
public sealed partial class ErasedPersonMessageSweepTests : IAsyncDisposable
{
    private const string Module = "Learning";
    private const string Schema = "learning";

    private static readonly DateTimeOffset Start = new(2027, 3, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan PassTimeout = TimeSpan.FromSeconds(30);
    private static readonly IReadOnlyCollection<Type> Types = [typeof(AnnouncedV1), typeof(string)];

    private readonly TimerSignallingTimeProvider time = new(Start);
    private readonly IPersonalMessagePurge purge = Substitute.For<IPersonalMessagePurge>();
    private readonly FakeLogger<ErasedPersonMessageSweep> logger = new();
    private readonly List<TaskCompletionSource<Pass>> passes = [];
    private readonly List<Func<CancellationToken, Task<int>>> behaviours = [];
    private readonly Lock gate = new();
    private readonly ErasedPersonMessageSweep service;
    private int ran;

    public ErasedPersonMessageSweepTests()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection([new KeyValuePair<string, string?>("Learning:ErasedPersonMessageSweep:Interval", "00:00:10")])
            .Build();
        purge.PurgeErasedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyCollection<Type>>(), Arg.Any<CancellationToken>())
            .Returns(call => Run(call.ArgAt<string>(0), call.ArgAt<IReadOnlyCollection<Type>>(1), call.ArgAt<CancellationToken>(2)));
        service = new ErasedPersonMessageSweep(Module, Schema, Types, ErasedPersonMessageSweepSettings.From(configuration, Module), purge, time, logger);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>Stopping waits for the loop to end, so whatever it would have run before the first interval has run by then.</summary>
    [Fact]
    public async Task No_pass_runs_before_the_first_interval()
    {
        await StartAsync();

        time.Advance(Interval - TimeSpan.FromMilliseconds(1));
        await service.StopAsync(Token);

        purge.ReceivedCalls().ShouldBeEmpty("a pass ran before one interval had passed since the start");
    }

    [Fact]
    public async Task One_pass_runs_per_interval_over_the_modules_schema_and_types()
    {
        var expected = new[] { Expect(), Expect(), Expect() };
        await StartAsync();

        foreach (var (pass, number) in expected.Select((pass, index) => (pass, index + 1)))
        {
            time.Advance(Interval);
            var ranAt = await pass;
            ranAt.At.ShouldBe(Start + (Interval * number));
            ranAt.Schema.ShouldBe(Schema);
            ranAt.Types.ShouldBeSameAs(Types);
        }

        ran.ShouldBe(3);
    }

    [Fact]
    public async Task A_pass_that_removed_messages_is_logged_with_the_count_and_the_module_only()
    {
        var removing = Expect(_ => Task.FromResult(4));
        var next = Expect();
        await StartAsync();

        time.Advance(Interval);
        await removing;
        time.Advance(Interval);
        await next;

        var logged = logger.Collector.GetSnapshot().ShouldHaveSingleItem();
        logged.Level.ShouldBe(LogLevel.Information);
        logged.Message.ShouldBe("Removed 4 stored messages of erased persons from Learning's message storage.");
        AnyGuid().IsMatch(logged.Message).ShouldBeFalse("the log names a person");
        logged.Exception.ShouldBeNull();
    }

    [Fact]
    public async Task A_pass_that_removed_nothing_logs_nothing()
    {
        var expected = new[] { Expect(), Expect(), Expect() };
        await StartAsync();

        foreach (var pass in expected)
        {
            time.Advance(Interval);
            await pass;
        }

        logger.Collector.GetSnapshot().ShouldBeEmpty("a pass that removed nothing was logged");
    }

    [Fact]
    public async Task A_pass_that_throws_is_logged_as_an_error_and_the_next_interval_runs_again()
    {
        var broken = new InvalidOperationException("The database is gone.");
        var first = Expect(_ => Task.FromException<int>(broken));
        var second = Expect();
        await StartAsync();

        time.Advance(Interval);
        await first;
        time.Advance(Interval);
        await second;

        var logged = logger.Collector.GetSnapshot().ShouldHaveSingleItem();
        logged.Level.ShouldBe(LogLevel.Error);
        logged.Message.ShouldBe("A pass of Learning's sweep of erased persons' messages failed; the next interval runs it again.");
        logged.Exception.ShouldBeSameAs(broken);
        service.ExecuteTask.ShouldNotBeNull().IsCompleted.ShouldBeFalse("the loop ended after a pass threw");
    }

    [Fact]
    public async Task Stopping_ends_the_loop_without_an_error()
    {
        var running = Expect(async cancellationToken =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        });
        await StartAsync();
        time.Advance(Interval);
        await running;

        await service.StopAsync(Token);

        var loop = service.ExecuteTask.ShouldNotBeNull();
        loop.IsCompleted.ShouldBeTrue("the loop went on after the host stopped");
        loop.IsFaulted.ShouldBeFalse(loop.Exception?.ToString());
        logger.Collector.GetSnapshot().ShouldNotContain(record => record.Level >= LogLevel.Warning, "stopping the host was logged as a failure");
    }

    public async ValueTask DisposeAsync()
    {
        await service.StopAsync(CancellationToken.None);
        service.Dispose();
    }

    [GeneratedRegex("[0-9a-fA-F]{8}-?[0-9a-fA-F]{4}-?[0-9a-fA-F]{4}-?[0-9a-fA-F]{4}-?[0-9a-fA-F]{12}")]
    private static partial Regex AnyGuid();

    /// <summary>Starts the service and waits until its timer exists, so the clock moves only once it counts.</summary>
    private async Task StartAsync()
    {
        await service.StartAsync(Token);
        await time.TimerCreated.WaitAsync(PassTimeout, Token);
    }

    /// <summary>The next pass, which completes when the service asks the purge; <paramref name="behaviour"/> answers it, by default with 0 rows.</summary>
    private Task<Pass> Expect(Func<CancellationToken, Task<int>>? behaviour = null)
    {
        var pass = new TaskCompletionSource<Pass>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (gate)
        {
            passes.Add(pass);
            behaviours.Add(behaviour ?? (_ => Task.FromResult(0)));
        }

        return pass.Task.WaitAsync(PassTimeout, Token);
    }

    private async Task<Option<int>> Run(string schema, IReadOnlyCollection<Type> types, CancellationToken cancellationToken)
    {
        TaskCompletionSource<Pass> pass;
        Func<CancellationToken, Task<int>> behaviour;
        lock (gate)
        {
            var number = ran++;
            if (number >= passes.Count)
            {
                throw new InvalidOperationException($"Pass {number + 1} ran, but the test expected {passes.Count}.");
            }

            pass = passes[number];
            behaviour = behaviours[number];
        }

        pass.TrySetResult(new Pass(time.GetUtcNow(), schema, types));
        return Option<int>.Some(await behaviour(cancellationToken));
    }

    /// <summary>A pass as the purge saw it: when, over which schema and which message types.</summary>
    private sealed record Pass(DateTimeOffset At, string Schema, IReadOnlyCollection<Type> Types);
}
