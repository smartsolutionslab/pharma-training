using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.ReadModels;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations.Events;
using Marten;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Infrastructure.EventStore;

/// <summary>
/// The stored ExaminationPassed events without a marker (#277): one anti-join of Marten's events against
/// <c>announced_events</c>, both in the module's schema of its database, oldest first; each found event is then loaded
/// through Marten, so its data is read as Marten stored it. It takes the store alone, so the module's event store
/// registers it once.
/// </summary>
public sealed class UnannouncedTestPasses(IDocumentStore store) : IUnannouncedTestPasses
{
    /// <summary>The module's event store and its tables live in this schema.</summary>
    public const string Schema = AssessmentAggregateStore.Schema;

    /// <summary>Marten's stored name of <see cref="ExaminationPassed"/>.</summary>
    private const string StoredType = "examination_passed";

    private const string Unannounced = $"""
        select e.id
        from {Schema}.mt_events e
        where e.type = ? and e.timestamp >= ? and e.timestamp <= ?
          and not exists (select 1 from {Schema}.announced_events a
                          where a.stream_id = e.stream_id and a.stream_version = e.version)
        order by e.seq_id
        limit ?
        """;

    public async Task<IReadOnlyList<StoredTestPass>> FindAsync(DateTimeOffset storedFrom, DateTimeOffset storedUntil, int limit, CancellationToken cancellationToken)
    {
        await using var session = store.QuerySession();
        var ids = await session.AdvancedSql.QueryAsync<Guid>(Unannounced, cancellationToken, StoredType, storedFrom.ToUniversalTime(), storedUntil.ToUniversalTime(), limit);
        List<StoredTestPass> found = [];
        foreach (var id in ids)
        {
            var stored = await session.Events.LoadAsync(id, cancellationToken)
                ?? throw new InvalidOperationException($"Assessment's event store lost event {id} between finding and loading it.");
            found.Add(new StoredTestPass((ExaminationPassed)stored.Data, stored.Version, stored.Timestamp));
        }

        return found;
    }
}
