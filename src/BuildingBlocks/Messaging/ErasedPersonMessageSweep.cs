using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;

/// <summary>
/// A module's sweep of erased persons' messages (#439, docs/adr/0036): every interval, starting one interval after start,
/// it asks <see cref="IPersonalMessagePurge.PurgeErasedAsync"/> to delete the dead letters and handled envelopes of the
/// module's personal message types whose body holds the id of a person in the module's tombstones. It removes what a
/// consumer's or the erasure's own purge could not yet see: a message dead-lettered after the purge, and the handled
/// envelope of a message a consumer dropped. A pass that removed rows is logged with their count and the module, never
/// whose they were; one that removed nothing logs nothing. A pass that throws never stops the host: it is logged as an
/// error and the next interval runs again. Stopping the host ends the loop quietly.
/// </summary>
public sealed partial class ErasedPersonMessageSweep : BackgroundService
{
    private readonly string module;
    private readonly string erasedPersonsSchema;
    private readonly IReadOnlyCollection<Type> types;
    private readonly ErasedPersonMessageSweepSettings settings;
    private readonly IPersonalMessagePurge purge;
    private readonly TimeProvider time;
    private readonly ILogger<ErasedPersonMessageSweep> logger;

    public ErasedPersonMessageSweep(
        string module,
        string erasedPersonsSchema,
        IReadOnlyCollection<Type> types,
        ErasedPersonMessageSweepSettings settings,
        IPersonalMessagePurge purge,
        TimeProvider time,
        ILogger<ErasedPersonMessageSweep> logger)
    {
        Ensure.That(module, nameof(module)).IsNotNullOrWhiteSpace();
        Ensure.That(erasedPersonsSchema, nameof(erasedPersonsSchema)).IsNotNullOrWhiteSpace();
        Ensure.That(types).IsNotNull();
        Ensure.That(settings).IsNotNull();
        Ensure.That(purge).IsNotNull();
        Ensure.That(time).IsNotNull();
        Ensure.That(logger).IsNotNull();

        this.module = module;
        this.erasedPersonsSchema = erasedPersonsSchema;
        this.types = types;
        this.settings = settings;
        this.purge = purge;
        this.time = time;
        this.logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(settings.Interval, time);
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

    private static bool IsStopping(Exception exception, CancellationToken stoppingToken) =>
        exception is OperationCanceledException && stoppingToken.IsCancellationRequested;

    private async Task PassAsync(CancellationToken stoppingToken)
    {
        try
        {
            var removed = await purge.PurgeErasedAsync(erasedPersonsSchema, types, stoppingToken);
            if (removed > 0)
            {
                LogRemoved(logger, removed, module);
            }
        }
        catch (Exception exception) when (!IsStopping(exception, stoppingToken))
        {
            LogFailed(logger, module, exception);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Removed {Count} stored messages of erased persons from {Module}'s message storage.")]
    private static partial void LogRemoved(ILogger logger, int count, string module);

    [LoggerMessage(Level = LogLevel.Error, Message = "A pass of {Module}'s sweep of erased persons' messages failed; the next interval runs it again.")]
    private static partial void LogFailed(ILogger logger, string module, Exception exception);
}
