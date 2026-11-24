using SmartSolutionsLab.PharmaTraining.BuildingBlocks.EventSourcing;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application;

/// <summary>
/// The stored events of one type, <typeparamref name="TEvent"/>, of Assessment's examinations that have no marker in
/// <c>announced_events</c> (#277, #307): their announcement never left. Unlike a pass, a failure and a lock are announced
/// with what their examination knew when they were stored (the attempt's number, the course), so each is loaded with its
/// examination. Implemented in the module's Infrastructure project over its event store, once per swept type.
/// </summary>
/// <typeparam name="TEvent">The stored event the sweep looks for.</typeparam>
public interface IUnannouncedExaminationEvents<TEvent>
    where TEvent : class, IDomainEvent
{
    /// <summary>
    /// Those stored from <paramref name="storedFrom"/> to <paramref name="storedUntil"/> and after <paramref name="after"/>
    /// in the event store's sequence (<see cref="UnannouncedEvent.Sequence"/>), oldest first, at most
    /// <paramref name="limit"/>; their data is not read.
    /// </summary>
    Task<IReadOnlyList<UnannouncedEvent>> FindAsync(DateTimeOffset storedFrom, DateTimeOffset storedUntil, long after, int limit, CancellationToken cancellationToken);

    /// <summary>
    /// The stored event <paramref name="found"/> names, as Marten stored it, with its examination replayed up to it; throws
    /// when either cannot be read, and hands a database failure over as the <see cref="System.Data.Common.DbException"/> it is.
    /// </summary>
    Task<StoredExaminationEvent<TEvent>> LoadAsync(UnannouncedEvent found, CancellationToken cancellationToken);
}
