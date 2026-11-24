namespace SmartSolutionsLab.PharmaTraining.Contracts.Organisation;

/// <summary>Organisation started a person's membership in a pharmacy; consumed by Compliance, Certification and Learning.</summary>
public sealed record MembershipStartedV1(Guid MembershipId, Guid PersonId, Guid PharmacyId, string DisplayName, JobFunctionV1 JobFunction, MembershipRoleV1 Role, DateTimeOffset StartedAt) : IntegrationEvent;
