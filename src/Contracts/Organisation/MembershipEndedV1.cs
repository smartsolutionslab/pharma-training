namespace SmartSolutionsLab.PharmaTraining.Contracts.Organisation;

/// <summary>Organisation ended a person's membership in a pharmacy; consumed by Compliance and Certification.</summary>
public sealed record MembershipEndedV1(Guid MembershipId, Guid PersonId, Guid PharmacyId, DateTimeOffset EndedAt) : IntegrationEvent;
