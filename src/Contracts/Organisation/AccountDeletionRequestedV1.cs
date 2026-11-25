namespace SmartSolutionsLab.PharmaTraining.Contracts.Organisation;

/// <summary>
/// Organisation forgot an erased person and announced the erasure, and the person's Keycloak account is due to be
/// deleted. Organisation's own work item, published in the same commit as <see cref="PersonErasureRequestedV1"/>: its
/// own consumer deletes the account and records the erasure's step <c>keycloak</c>. It never leaves the Organisation
/// service (a module of the host consumes it, so Wolverine routes it to the host's durable local queue and never to the
/// broker), so no other module can consume it. It carries ids only, never an address or a name, so its envelope and its
/// dead letter hold no personal data.
/// </summary>
public sealed record AccountDeletionRequestedV1(Guid ErasureId, Guid PersonId) : IntegrationEvent;
