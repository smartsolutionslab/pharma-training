namespace SmartSolutionsLab.PharmaTraining.Contracts.Organisation;

/// <summary>Organisation ended a person's membership in a pharmacy; consumed by Compliance, Certification and Learning.</summary>
public sealed record MembershipEndedV1(Guid MembershipId, Guid PersonId, Guid PharmacyId, DateTimeOffset EndedAt) : IntegrationEvent;
