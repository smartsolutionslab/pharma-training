using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.EventSourcing;
using SmartSolutionsLab.PharmaTraining.Contracts;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.ReadModels;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.LearningSessions;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Tests.LearningSessions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Tests.Sessions;

/// <summary>
/// Learning's session streams in memory, and their views as the inline projection keeps them: a view is the stream
/// folded through the projection, so it follows every save at once, as Marten's inline projection does. It is also the
/// module's unit of work: what is published is sent only once committed. <see cref="Steps"/> records saves, publishes
/// and commits in the order they happened. <see cref="LoseNextSave"/> makes the next save lose a race on its stream, as
/// an append does when another one landed since the session was loaded, or as a deadlock when given one:
/// <see cref="TrySaveAsync"/> answers how it lost, and <see cref="SaveAsync"/> throws, as Marten's expected-version check does.
/// </summary>
internal sealed class InMemoryLearningSessions : ILearningAggregateStore, ILearningSessions, ILearningUnitOfWork
{
    public const string Saved = "save";
    public const string Queued = "publish";
    public const string Committed = "commit";

    private readonly Dictionary<Guid, List<IDomainEvent>> streams = [];
    private readonly List<IntegrationEvent> queued = [];
    private bool loseNextSave;

    private LostRace how = LostRaces.VersionMovedOn();
    private CancellationTokenSource? cancelledAfterNextSave;

    public IReadOnlyDictionary<Guid, List<IDomainEvent>> Streams => streams;

    public int Saves { get; private set; }

    /// <summary>How many times a stream was asked for, found or not.</summary>
    public int Loads { get; private set; }

    public List<IntegrationEvent> Published { get; } = [];

    public int Commits { get; private set; }

    public List<string> Steps { get; } = [];

    public Task<Option<TAggregate>> LoadAsync<TAggregate>(IAggregateId id, CancellationToken cancellationToken)
        where TAggregate : class, IEventSourced
    {
        Loads++;
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

    /// <summary>What the module's store answers once it opts in to lost races: how the race was lost, with nothing stored and the aggregate left unmarked.</summary>
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

    /// <summary>The caller goes away (the request is aborted) just after the next save has stored its events.</summary>
    public void CancelAfterNextSave(CancellationTokenSource caller) => cancelledAfterNextSave = caller;

    public Task<IReadOnlyList<LearningSessionView>> ListAsync(PersonId learner, CourseVersionId version, CancellationToken cancellationToken)
    {
        IReadOnlyList<LearningSessionView> views = [.. streams.Values
            .Select(LearningSessionViews.Project)
            .Where(view => view.LearnerId == learner.Value && view.CourseVersionId == version.Value)];
        return Task.FromResult(views);
    }

    public Task<Option<LearningSessionView>> FindAsync(LearningSessionId id, CancellationToken cancellationToken) =>
        Task.FromResult(streams.TryGetValue(id.Value, out var stream)
            ? Option<LearningSessionView>.Some(LearningSessionViews.Project(stream))
            : Option<LearningSessionView>.None);

    public Task PublishAsync(IntegrationEvent integrationEvent)
    {
        queued.Add(integrationEvent);
        Steps.Add(Queued);
        return Task.CompletedTask;
    }

    /// <summary>Like the outbox's own commit, a cancelled token commits nothing.</summary>
    public Task CommitAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Published.AddRange(queued);
        queued.Clear();
        Commits++;
        Steps.Add(Committed);
        return Task.CompletedTask;
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
        if (cancelledAfterNextSave is { } caller)
        {
            cancelledAfterNextSave = null;
            caller.Cancel();
        }
    }
}
