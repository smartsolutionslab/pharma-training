namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;

/// <summary>
/// Removes a person's personal data from Wolverine's storage of a service's database (#115): the dead letters, and the
/// handled envelopes of the durable inbox, of the given message types whose body holds the person's id. The outbox is
/// never touched.
/// </summary>
public interface IPersonalMessagePurge
{
    Task PurgeAsync(Guid personId, IReadOnlyCollection<Type> types, CancellationToken cancellationToken);

    /// <summary>
    /// The sweep of erased persons' messages (#439): deletes, in one transaction, the dead letters and the handled
    /// envelopes of the durable inbox, of the given message types, whose body holds the id of any person in
    /// <c>&lt;erasedPersonsSchema&gt;.erased_persons</c>, and answers how many rows it deleted, or nothing when another sweep
    /// of the same database holds the sweep's lock and this pass was skipped (#482: a skipped pass is not a pass that found
    /// nothing). The outbox and envelopes still waiting are never touched.
    /// </summary>
    Task<Option<int>> PurgeErasedAsync(string erasedPersonsSchema, IReadOnlyCollection<Type> types, CancellationToken cancellationToken);
}
