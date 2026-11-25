using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.EventSourcing;
using SmartSolutionsLab.PharmaTraining.Contracts;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.ReadModels;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.Samples;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Tests.Samples;

/// <summary>
/// The module's event streams and its samples' table in memory, with its unit of work over the table: events count once
/// they are saved, a row once it is committed.
/// </summary>
internal sealed class InMemorySamples : ILearningAggregateStore, ISampleSummaries, ILearningUnitOfWork
{
    private readonly Dictionary<Guid, List<IDomainEvent>> streams = [];
    private readonly Dictionary<Guid, SampleSummary> recorded = [];

    public IReadOnlyDictionary<Guid, List<IDomainEvent>> Streams => streams;

    public List<SampleSummary> Summaries { get; } = [];

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

    /// <summary>Saves like <see cref="SaveAsync"/>; a sample's stream never loses a race here, so it answers success.</summary>
    public async Task<Result<Unit, LostRace>> TrySaveAsync(IEventSourced aggregate, CancellationToken cancellationToken)
    {
        await SaveAsync(aggregate, cancellationToken);
        return Success(Unit.Value);
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

    public Task RecordAsync(Sample sample, CancellationToken cancellationToken)
    {
        recorded[sample.Id.Value] = new SampleSummary { Id = sample.Id.Value, Name = sample.Name.Value };
        return Task.CompletedTask;
    }

    public Task<Option<SampleSummary>> FindAsync(Guid id, CancellationToken cancellationToken) =>
        Summaries.SingleOrDefault(summary => summary.Id == id) is { } found ? Task.FromResult(Option<SampleSummary>.Some(found)) : Task.FromResult(Option<SampleSummary>.None);

    public Task<PagedResult<SampleSummary>> ListAsync(Page page, PageSize pageSize, CancellationToken cancellationToken)
    {
        List<SampleSummary> items = [.. Summaries
            .OrderBy(summary => summary.Name, StringComparer.Ordinal)
            .ThenBy(summary => summary.Id)
            .Skip(page.Skip(pageSize))
            .Take(pageSize.Value)];
        return Task.FromResult(new PagedResult<SampleSummary>(items, Summaries.Count, page.Value, pageSize.Value));
    }

    public Task PublishAsync(IntegrationEvent integrationEvent) => Task.CompletedTask;

    public Task CommitAsync(CancellationToken cancellationToken)
    {
        Summaries.AddRange(recorded.Values);
        recorded.Clear();
        return Task.CompletedTask;
    }
}
