using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;

/// <summary>
/// Sends <typeparamref name="TCommand"/>, a command without data, every interval, through the dispatcher of a scope of
/// its own; the first pass runs one interval after start, never at start. It runs a module's announcement sweep (#277,
/// docs/adr/0016). A pass that throws never stops the host: one that ends on a unique violation of the marker's primary
/// key (another writer, a replica's sweep or the late handler, committed the marker first) is logged at Information,
/// anything else at Error, a unique violation of another constraint included, and the next interval runs again.
/// Stopping the host ends the loop quietly.
/// </summary>
public sealed partial class PeriodicCommandService<TCommand> : BackgroundService
    where TCommand : ICommand<Result<Unit, ApiError>>, new()
{
    private readonly TimeSpan interval;
    private readonly IServiceScopeFactory scopes;
    private readonly TimeProvider time;
    private readonly ILogger<PeriodicCommandService<TCommand>> logger;

    public PeriodicCommandService(TimeSpan interval, IServiceScopeFactory scopes, TimeProvider time, ILogger<PeriodicCommandService<TCommand>> logger)
    {
        Ensure.That(scopes).IsNotNull();
        Ensure.That(time).IsNotNull();
        Ensure.That(logger).IsNotNull();

        this.interval = interval;
        this.scopes = scopes;
        this.time = time;
        this.logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(interval, time);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await PassAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // The host is stopping: the loop ends without a pass of its own.
        }
    }

    private static bool IsUniqueViolation(Exception exception) => exception switch
    {
        PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: AnnouncedEventMarker.PrimaryKey } => true,
        AggregateException aggregate => aggregate.InnerExceptions.Any(IsUniqueViolation),
        { InnerException: { } inner } => IsUniqueViolation(inner),
        _ => false,
    };

    private static bool IsStopping(Exception exception, CancellationToken stoppingToken) =>
        exception is OperationCanceledException && stoppingToken.IsCancellationRequested;

    private async Task PassAsync(CancellationToken stoppingToken)
    {
        var command = typeof(TCommand).Name;
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
            var passed = await dispatcher.SendAsync(new TCommand(), stoppingToken);
            if (passed.IsFailure)
            {
                LogRefused(logger, command, passed.Error.Code);
            }
        }
        catch (Exception exception) when (IsUniqueViolation(exception))
        {
            LogLostRace(logger, command);
        }
        catch (Exception exception) when (!IsStopping(exception, stoppingToken))
        {
            LogFailed(logger, command, exception);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "A pass of {Command} ended on a unique violation: another writer announced the event first. The next pass finds its marker.")]
    private static partial void LogLostRace(ILogger logger, string command);

    [LoggerMessage(Level = LogLevel.Error, Message = "A pass of {Command} failed; the next interval runs it again.")]
    private static partial void LogFailed(ILogger logger, string command, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "A pass of {Command} answered {Code}; the next interval runs it again.")]
    private static partial void LogRefused(ILogger logger, string command, string code);
}
