using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Npgsql;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging.Tests;

/// <summary>
/// A module's announcement sweep (#277) is a command without data that a hosted service sends every interval of the
/// module's <c>AnnouncementSweep</c> settings, through the dispatcher of a scope of its own, starting one interval after
/// start. A pass that throws never stops the host: a unique violation (another writer announced the event first) is
/// logged at Information, anything else at Error, and the next interval runs again. The clock is a
/// <see cref="TimerSignallingTimeProvider"/>, moved by hand; each pass completes a task the test awaits.
/// </summary>
public sealed class PeriodicCommandServiceTests : IAsyncDisposable
{
    private static readonly DateTimeOffset Start = new(2027, 3, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan PassTimeout = TimeSpan.FromSeconds(30);

    private readonly TimerSignallingTimeProvider time = new(Start);
    private readonly List<TaskCompletionSource<Pass>> passes = [];
    private readonly List<Func<CancellationToken, Task<Result<Unit, ApiError>>>> behaviours = [];
    private readonly Lock gate = new();
    private readonly ServiceProvider provider;
    private int sent;

    public PeriodicCommandServiceTests()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection([new KeyValuePair<string, string?>("Test:AnnouncementSweep:Interval", "00:00:10")])
            .Build();
        var services = new ServiceCollection();
        services.AddFakeLogging();
        services.AddSingleton<TimeProvider>(time);
        services.AddScoped(_ => NewDispatcher());
        services.AddAnnouncementSweep<Tick>(configuration, "Test");
        provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        Service = provider.GetServices<IHostedService>().OfType<PeriodicCommandService<Tick>>().ShouldHaveSingleItem();
    }

    private PeriodicCommandService<Tick> Service { get; }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>Stopping waits for the loop to end, so whatever it would have sent before the first interval has been sent by then.</summary>
    [Fact]
    public async Task No_pass_runs_before_the_first_interval()
    {
        await StartAsync();

        time.Advance(Interval - TimeSpan.FromMilliseconds(1));
        await Service.StopAsync(Token);

        sent.ShouldBe(0, "a pass ran before one interval had passed since the start");
    }

    [Fact]
    public async Task One_pass_runs_per_interval()
    {
        var expected = new[] { Expect(), Expect(), Expect() };
        await StartAsync();

        foreach (var (pass, number) in expected.Select((pass, index) => (pass, index + 1)))
        {
            time.Advance(Interval);
            (await pass).SentAt.ShouldBe(Start + (Interval * number));
        }

        sent.ShouldBe(3);
    }

    [Fact]
    public async Task A_pass_that_throws_is_logged_and_the_next_interval_runs_again()
    {
        var broken = new InvalidOperationException("The event store is gone.");
        var first = Expect(_ => Task.FromException<Result<Unit, ApiError>>(broken));
        var second = Expect();
        await StartAsync();

        time.Advance(Interval);
        await first;
        time.Advance(Interval);
        await second;

        var logged = Logs().Where(record => record.Level >= LogLevel.Warning).ShouldHaveSingleItem();
        logged.Level.ShouldBe(LogLevel.Error);
        logged.Exception.ShouldBeSameAs(broken);
        Service.ExecuteTask.ShouldNotBeNull().IsCompleted.ShouldBeFalse("the loop ended after a pass threw");
    }

    [Fact]
    public async Task A_pass_ending_on_a_unique_violation_is_logged_as_information()
    {
        var lostRace = new DbUpdateException(
            "An error occurred while saving the entity changes.",
            new PostgresException("duplicate key value violates unique constraint \"pk_announced_events\"", "ERROR", "ERROR", PostgresErrorCodes.UniqueViolation, constraintName: "pk_announced_events"));
        var first = Expect(_ => Task.FromException<Result<Unit, ApiError>>(lostRace));
        var second = Expect();
        await StartAsync();

        time.Advance(Interval);
        await first;
        time.Advance(Interval);
        await second;

        Logs().ShouldNotContain(record => record.Level >= LogLevel.Warning, "a pass that lost the race to another writer was logged as a failure");
        Logs().ShouldContain(record => record.Level == LogLevel.Information);
        Service.ExecuteTask.ShouldNotBeNull().IsCompleted.ShouldBeFalse("the loop ended after a unique violation");
    }

    [Fact]
    public async Task A_pass_ending_on_a_unique_violation_of_another_constraint_is_logged_as_an_error()
    {
        var other = new DbUpdateException(
            "An error occurred while saving the entity changes.",
            new PostgresException("duplicate key value violates unique constraint \"pk_wolverine_outgoing_envelopes\"", "ERROR", "ERROR", PostgresErrorCodes.UniqueViolation, constraintName: "pk_wolverine_outgoing_envelopes"));
        var first = Expect(_ => Task.FromException<Result<Unit, ApiError>>(other));
        var second = Expect();
        await StartAsync();

        time.Advance(Interval);
        await first;
        time.Advance(Interval);
        await second;

        var logged = Logs().Where(record => record.Level >= LogLevel.Warning).ShouldHaveSingleItem("only the marker's primary key means another writer announced the event first");
        logged.Level.ShouldBe(LogLevel.Error);
        logged.Exception.ShouldBeSameAs(other);
        Service.ExecuteTask.ShouldNotBeNull().IsCompleted.ShouldBeFalse("the loop ended after a unique violation");
    }

    [Fact]
    public async Task Each_pass_runs_in_a_scope_of_its_own()
    {
        var expected = new[] { Expect(), Expect() };
        await StartAsync();

        List<IDispatcher> used = [];
        foreach (var pass in expected)
        {
            time.Advance(Interval);
            used.Add((await pass).Dispatcher);
        }

        used.Distinct().Count().ShouldBe(2, "two passes shared one scope's dispatcher");
    }

    [Fact]
    public async Task Stopping_ends_the_loop_without_an_error()
    {
        var running = Expect(async cancellationToken =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return Success(Unit.Value);
        });
        await StartAsync();
        time.Advance(Interval);
        await running;

        await Service.StopAsync(Token);

        var loop = Service.ExecuteTask.ShouldNotBeNull();
        loop.IsCompleted.ShouldBeTrue("the loop went on after the host stopped");
        loop.IsFaulted.ShouldBeFalse(loop.Exception?.ToString());
        Logs().ShouldNotContain(record => record.Level >= LogLevel.Warning, "stopping the host was logged as a failure");
    }

    public async ValueTask DisposeAsync()
    {
        await Service.StopAsync(CancellationToken.None);
        await provider.DisposeAsync();
    }

    /// <summary>Starts the service and waits until its timer exists, so the clock moves only once it counts.</summary>
    private async Task StartAsync()
    {
        await Service.StartAsync(Token);
        await time.TimerCreated.WaitAsync(PassTimeout, Token);
    }

    /// <summary>The next pass, which completes when the service sends its command; <paramref name="behaviour"/> answers it, by default with success.</summary>
    private Task<Pass> Expect(Func<CancellationToken, Task<Result<Unit, ApiError>>>? behaviour = null)
    {
        var pass = new TaskCompletionSource<Pass>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (gate)
        {
            passes.Add(pass);
            behaviours.Add(behaviour ?? (_ => Task.FromResult<Result<Unit, ApiError>>(Success(Unit.Value))));
        }

        return pass.Task.WaitAsync(PassTimeout, Token);
    }

    private IDispatcher NewDispatcher()
    {
        var dispatcher = Substitute.For<IDispatcher>();
        dispatcher.SendAsync(Arg.Any<ICommand<Result<Unit, ApiError>>>(), Arg.Any<CancellationToken>())
            .Returns(call => Send(dispatcher, call.ArgAt<ICommand<Result<Unit, ApiError>>>(0), call.ArgAt<CancellationToken>(1)));
        return dispatcher;
    }

    private Task<Result<Unit, ApiError>> Send(IDispatcher dispatcher, ICommand<Result<Unit, ApiError>> command, CancellationToken cancellationToken)
    {
        command.ShouldBeOfType<Tick>();
        TaskCompletionSource<Pass> pass;
        Func<CancellationToken, Task<Result<Unit, ApiError>>> behaviour;
        lock (gate)
        {
            var number = sent++;
            if (number >= passes.Count)
            {
                throw new InvalidOperationException($"Pass {number + 1} ran, but the test expected {passes.Count}.");
            }

            pass = passes[number];
            behaviour = behaviours[number];
        }

        pass.TrySetResult(new Pass(time.GetUtcNow(), dispatcher));
        return behaviour(cancellationToken);
    }

    private IReadOnlyList<FakeLogRecord> Logs() => provider.GetFakeLogCollector().GetSnapshot();

    /// <summary>The command a test's sweep sends: no data, like a module's.</summary>
    public sealed record Tick : ICommand<Result<Unit, ApiError>>;

    /// <summary>A pass as the dispatcher saw it: when, and through which scope's dispatcher.</summary>
    private sealed record Pass(DateTimeOffset SentAt, IDispatcher Dispatcher);
}
