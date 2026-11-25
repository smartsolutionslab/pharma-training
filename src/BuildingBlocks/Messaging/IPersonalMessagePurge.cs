namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;

/// <summary>
/// Removes a person's personal data from Wolverine's storage of a service's database (#115): the dead letters, and the
/// handled envelopes of the durable inbox, of the given message types whose body holds the person's id. The outbox is
/// never touched.
/// </summary>
public interface IPersonalMessagePurge
{
    Task PurgeAsync(Guid personId, IReadOnlyCollection<Type> types, CancellationToken cancellationToken);
}
