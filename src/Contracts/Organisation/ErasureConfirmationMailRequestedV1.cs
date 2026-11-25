namespace SmartSolutionsLab.PharmaTraining.Contracts.Organisation;

/// <summary>
/// Support requested a person's erasure, and the confirmation mail to the person's addresses is due before Organisation
/// forgets them. Organisation's own work item: its own consumer mails the confirmation, then forgets the person's name
/// and addresses and announces the erasure as <see cref="PersonErasureRequestedV1"/>. It never leaves the Organisation
/// service (a module of the host consumes it, so Wolverine routes it to the host's durable local queue and never to the
/// broker), so no other module can consume it. It carries ids only, never an address or a name, so its envelope and its
/// dead letter hold no personal data.
/// </summary>
public sealed record ErasureConfirmationMailRequestedV1(Guid ErasureId, Guid PersonId) : IntegrationEvent;
