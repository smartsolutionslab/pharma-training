using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging.Tests;

/// <summary>
/// A module runs a command without data every interval of its own choosing (#115, slice 5: Certification's purge of
/// erased persons' certificates) with <c>AddPeriodicCommand</c>: a <see cref="PeriodicCommandService{TCommand}"/> hosted
/// service, on the container's clock, or the system's where the container has none.
/// </summary>
public sealed class PeriodicCommandsTests
{
    private static readonly DateTimeOffset Start = new(2027, 3, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    [Fact]
    public void The_command_runs_as_a_hosted_service()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddPeriodicCommand<Purge>(Interval);

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        provider.GetServices<IHostedService>().OfType<PeriodicCommandService<Purge>>().ShouldHaveSingleItem();
        provider.GetRequiredService<TimeProvider>().ShouldBeSameAs(TimeProvider.System);
    }

    /// <summary>The service sends the command once the interval it was given has passed on the container's clock, not before.</summary>
    [Fact]
    public async Task A_pass_runs_once_the_given_interval_has_passed_on_the_containers_clock()
    {
        var time = new TimerSignallingTimeProvider(Start);
        var sent = new TaskCompletionSource<DateTimeOffset>(TaskCreationOptions.RunContinuationsAsynchronously);
        var dispatcher = Substitute.For<IDispatcher>();
        dispatcher.SendAsync(Arg.Any<Purge>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            sent.TrySetResult(time.GetUtcNow());
            return Task.FromResult<Result<Unit, ApiError>>(Success(Unit.Value));
        });
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(time);
        services.AddScoped(_ => dispatcher);
        services.AddPeriodicCommand<Purge>(Interval);
        await using var provider = services.BuildServiceProvider();
        var service = provider.GetServices<IHostedService>().OfType<PeriodicCommandService<Purge>>().ShouldHaveSingleItem();
        await service.StartAsync(TestContext.Current.CancellationToken);
        try
        {
            await time.TimerCreated.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
            time.Advance(Interval - TimeSpan.FromSeconds(1));
            sent.Task.IsCompleted.ShouldBeFalse("a pass ran before the interval had passed");

            time.Advance(TimeSpan.FromSeconds(1));

            (await sent.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken)).ShouldBe(Start + Interval);
        }
        finally
        {
            await service.StopAsync(TestContext.Current.CancellationToken);
        }
    }

    /// <summary>A command a module would run every interval.</summary>
    public sealed record Purge : ICommand<Result<Unit, ApiError>>;
}
