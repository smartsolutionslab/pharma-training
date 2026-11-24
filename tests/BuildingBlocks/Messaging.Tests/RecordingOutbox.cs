using Microsoft.EntityFrameworkCore;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.Runtime;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging.Tests;

/// <summary>
/// The scope's EF Core outbox as the unit of work sees it: a real Wolverine <see cref="MessageContext"/>, so its
/// <see cref="MessageContext.MultiFlushMode"/> is Wolverine's own, over a substituted runtime and with no message store.
/// It keeps what is published and, on each save, records what that save's flush sends, by Wolverine 6.64.0's rule
/// (<c>MessageContext.beginFlushAsync</c>): the first flush sends every envelope still outstanding, and a later one sends
/// what was published since only under <see cref="MultiFlushMode.AllowMultiples"/>; under
/// <see cref="MultiFlushMode.OnlyOnce"/> it sends nothing and leaves them outstanding. The overload with a mode sets it
/// for that call alone and restores it afterwards, as Wolverine's <c>DbContextOutbox&lt;T&gt;</c> does.
/// </summary>
public sealed class RecordingOutbox(IWolverineRuntime runtime, FlushDbContext dbContext) : MessageContext(runtime), IDbContextOutbox<FlushDbContext>
{
    private readonly List<object> outstanding = [];
    private bool hasFlushed;

    public FlushDbContext DbContext { get; } = dbContext;

    /// <summary>What each save sent, one entry per save, in order; an empty entry is a save whose flush sent nothing.</summary>
    public List<IReadOnlyList<object>> Flushes { get; } = [];

    public Task SaveChangesAndFlushMessagesAsync(CancellationToken token = default)
    {
        if (hasFlushed && MultiFlushMode == MultiFlushMode.OnlyOnce)
        {
            Flushes.Add([]);
            return Task.CompletedTask;
        }

        Flushes.Add([.. outstanding]);
        outstanding.Clear();
        hasFlushed = true;
        return Task.CompletedTask;
    }

    public async Task SaveChangesAndFlushMessagesAsync(MultiFlushMode multiFlushMode, CancellationToken token = default)
    {
        var previous = MultiFlushMode;
        MultiFlushMode = multiFlushMode;
        try
        {
            await SaveChangesAndFlushMessagesAsync(token);
        }
        finally
        {
            MultiFlushMode = previous;
        }
    }

    ValueTask IMessageBus.PublishAsync<T>(T message, DeliveryOptions? options)
    {
        if (message is not null)
        {
            outstanding.Add(message);
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>A DbContext for the outbox that is never opened.</summary>
    public static FlushDbContext UnopenedContext() =>
        new(new DbContextOptionsBuilder<FlushDbContext>().UseNpgsql("Host=unused").Options);
}
