using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.EventSourcing;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Tests.Consumers;

/// <summary>
/// The module's event streams in memory: a save appends the aggregate's pending events to its stream, and a load
/// replays them into a new aggregate, as the event store does. <see cref="LoseNextSave"/> makes the next save lose a
/// race on its stream, as an append does when another one landed since the examination was loaded:
/// <see cref="TrySaveAsync"/> answers <see langword="false"/> with nothing stored, and <see cref="SaveAsync"/> throws.
/// Each save is recorded in <see cref="Steps"/>, which a unit of work may share to show the order.
/// </summary>
internal sealed class InMemoryAssessmentAggregateStore(List<string>? steps = null) : IAssessmentAggregateStore
{
    public const string Saved = "save";

    private readonly Dictionary<Guid, List<IDomainEvent>> streams = [];
    private bool loseNextSave;

    private LostRace how = LostRaces.VersionMovedOn();

    public IReadOnlyDictionary<Guid, List<IDomainEvent>> Streams => streams;

    public List<string> Steps { get; } = steps ?? [];

    public int Saves { get; private set; }

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
        if (loseNextSave)
        {
            loseNextSave = false;
            throw new InvalidOperationException($"The stream {aggregate.Id.Value} moved on since it was loaded.");
        }

        Append(aggregate);
        return Task.CompletedTask;
    }

    /// <summary>What the module's store answers once it opts in to lost races (ADR 0016): <see langword="false"/> with nothing stored and the aggregate left unmarked.</summary>
    public Task<Result<Unit, LostRace>> TrySaveAsync(IEventSourced aggregate, CancellationToken cancellationToken)
    {
        if (loseNextSave)
        {
            loseNextSave = false;
            return Task.FromResult<Result<Unit, LostRace>>(Failure(how));
        }

        Append(aggregate);
        return Task.FromResult<Result<Unit, LostRace>>(Success(Unit.Value));
    }

    /// <summary>The next save, whichever way it is made, loses the race on its stream and stores nothing.</summary>
    public void LoseNextSave(LostRace? how = null)
    {
        loseNextSave = true;
        this.how = how ?? LostRaces.VersionMovedOn();
    }

    private void Append(IEventSourced aggregate)
    {
        var history = streams.GetValueOrDefault(aggregate.Id.Value) ?? [];
        if (history.Count != aggregate.Version)
        {
            throw new InvalidOperationException($"The stream {aggregate.Id.Value} moved on since it was loaded.");
        }

        streams[aggregate.Id.Value] = [.. history, .. aggregate.PendingEvents];
        aggregate.MarkStored();
        Saves++;
        Steps.Add(Saved);
    }
}
