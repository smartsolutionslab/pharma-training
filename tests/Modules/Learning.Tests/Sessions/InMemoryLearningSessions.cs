using SmartSolutionsLab.PharmaTraining.BuildingBlocks.EventSourcing;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.ReadModels;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.LearningSessions;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Tests.LearningSessions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Tests.Sessions;

/// <summary>
/// Learning's session streams in memory, and their views as the inline projection keeps them: a view is the stream
/// folded through the projection, so it follows every save at once, as Marten's inline projection does.
/// </summary>
internal sealed class InMemoryLearningSessions : ILearningAggregateStore, ILearningSessions
{
    private readonly Dictionary<Guid, List<IDomainEvent>> streams = [];

    public IReadOnlyDictionary<Guid, List<IDomainEvent>> Streams => streams;

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
        var history = streams.GetValueOrDefault(aggregate.Id.Value) ?? [];
        if (history.Count != aggregate.Version)
        {
            throw new InvalidOperationException($"The stream {aggregate.Id.Value} moved on since it was loaded.");
        }

        streams[aggregate.Id.Value] = [.. history, .. aggregate.PendingEvents];
        aggregate.MarkStored();
        Saves++;
        return Task.CompletedTask;
    }

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
}
