using SmartSolutionsLab.PharmaTraining.BuildingBlocks.EventSourcing;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Tests.Consumers;

/// <summary>
/// The module's event streams in memory: a save appends the aggregate's pending events to its stream, and a load
/// replays them into a new aggregate, as the event store does.
/// </summary>
internal sealed class InMemoryAssessmentAggregateStore : IAssessmentAggregateStore
{
    private readonly Dictionary<Guid, List<IDomainEvent>> streams = [];

    public IReadOnlyDictionary<Guid, List<IDomainEvent>> Streams => streams;

    public Task<Option<TAggregate>> LoadAsync<TAggregate>(IAggregateId id, CancellationToken cancellationToken)
        where TAggregate : class, IEventSourced
    {
        if (!streams.TryGetValue(id.Value, out var history))
        {
            return Task.FromResult(Option<TAggregate>.None);
        }

        var aggregate = (TAggregate)Activator.CreateInstance(typeof(TAggregate), nonPublic: true)!;
        aggregate.Replay(history);
        return Task.FromResult(Option<TAggregate>.Some(aggregate));
    }

    public Task SaveAsync(IEventSourced aggregate, CancellationToken cancellationToken)
    {
        var history = streams.GetValueOrDefault(aggregate.Id.Value) ?? [];
        if (history.Count != aggregate.Version)
        {
            throw new InvalidOperationException($"The stream {aggregate.Id.Value} moved on since it was loaded.");
        }

        streams[aggregate.Id.Value] = [.. history, .. aggregate.PendingEvents];
        aggregate.MarkStored();
        return Task.CompletedTask;
    }
}
