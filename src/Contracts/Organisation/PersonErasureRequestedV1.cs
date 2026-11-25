namespace SmartSolutionsLab.PharmaTraining.Contracts.Organisation;

/// <summary>
/// Support asked Organisation to erase a person's data (GDPR Art. 17, #115), and Organisation has mailed the confirmation
/// and forgotten the person's name and addresses in its own tables. Every service that keeps a copy of the person's name
/// erases or pseudonymises it on this event: Compliance, Certification, Editorial and Learning, each in a later slice of
/// #115, so no module consumes it yet. It carries ids only, never a name or an address, so its envelope and its dead
/// letter hold no personal data; the PersonId stays behind as the pseudonym.
/// </summary>
public sealed record PersonErasureRequestedV1(Guid ErasureId, Guid PersonId) : IntegrationEvent;
