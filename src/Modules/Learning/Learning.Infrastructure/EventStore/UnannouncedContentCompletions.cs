using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.ReadModels;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.LearningSessions.Events;
using Marten;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Infrastructure.EventStore;

/// <summary>
/// The stored ContentCompleted events without a marker (#277): one anti-join of Marten's events against
/// <c>announced_events</c>, both in the module's schema of its database, oldest first; each found event is then loaded
/// through Marten, so its data is read as Marten stored it. It takes the store alone, so the module's event store
/// registers it once.
/// </summary>
public sealed class UnannouncedContentCompletions(IDocumentStore store) : IUnannouncedContentCompletions
{
    /// <summary>The module's event store and its tables live in this schema.</summary>
    public const string Schema = LearningAggregateStore.Schema;

    /// <summary>Marten's stored name of <see cref="ContentCompleted"/>.</summary>
    private const string StoredType = "content_completed";

    private const string Unannounced = $"""
        select e.id
        from {Schema}.mt_events e
        where e.type = ? and e.timestamp >= ? and e.timestamp <= ?
          and not exists (select 1 from {Schema}.announced_events a
                          where a.stream_id = e.stream_id and a.stream_version = e.version)
        order by e.seq_id
        limit ?
        """;

    public async Task<IReadOnlyList<StoredContentCompletion>> FindAsync(DateTimeOffset storedFrom, DateTimeOffset storedUntil, int limit, CancellationToken cancellationToken)
    {
        await using var session = store.QuerySession();
        var ids = await session.AdvancedSql.QueryAsync<Guid>(Unannounced, cancellationToken, StoredType, storedFrom.ToUniversalTime(), storedUntil.ToUniversalTime(), limit);
        List<StoredContentCompletion> found = [];
        foreach (var id in ids)
        {
            var stored = await session.Events.LoadAsync(id, cancellationToken)
                ?? throw new InvalidOperationException($"Learning's event store lost event {id} between finding and loading it.");
            found.Add(new StoredContentCompletion((ContentCompleted)stored.Data, stored.Version, stored.Timestamp));
        }

        return found;
    }
}
