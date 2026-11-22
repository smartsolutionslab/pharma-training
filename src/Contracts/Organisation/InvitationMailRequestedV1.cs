namespace SmartSolutionsLab.PharmaTraining.Contracts.Organisation;

/// <summary>
/// Organisation invited an address or renewed its invitation, and the mail with its link is due. Organisation's own
/// work item: its own consumer issues the link and mails it. It never leaves the Organisation service (a module of the
/// host consumes it, so Wolverine routes it to the host's durable local queue and never to the broker), so no other
/// module can consume it. It carries ids only, never the token, the address or a name, so its envelope and its dead
/// letter hold no personal data.
/// </summary>
public sealed record InvitationMailRequestedV1(Guid MembershipId, Guid PharmacyId) : IntegrationEvent;
