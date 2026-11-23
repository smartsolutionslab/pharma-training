namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.DTOs;

/// <summary>An invitation of a pharmacy nobody has accepted or revoked yet, open or run out, as its management sees it.</summary>
public sealed record PharmacyInvitationDto(
    Guid MembershipId,
    string Email,
    OrganisationJobFunction JobFunction,
    OrganisationMembershipRole Role,
    DateTimeOffset ExpiresAt);
