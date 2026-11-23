namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.DTOs;

/// <summary>An active member of a pharmacy, as its management sees them.</summary>
public sealed record PharmacyMemberDto(
    Guid MembershipId,
    Guid PersonId,
    string Name,
    OrganisationJobFunction JobFunction,
    OrganisationMembershipRole Role,
    DateTimeOffset Since);
