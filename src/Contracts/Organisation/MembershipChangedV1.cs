namespace SmartSolutionsLab.PharmaTraining.Contracts.Organisation;

/// <summary>Organisation changed an active membership's job function or role; Version counts the membership's states from 1 (its start), so the first change is 2. Consumed by Compliance.</summary>
public sealed record MembershipChangedV1(Guid MembershipId, Guid PersonId, Guid PharmacyId, string DisplayName, JobFunctionV1 JobFunction, MembershipRoleV1 Role, int Version) : IntegrationEvent;
