namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.DTOs;

/// <summary>A pharmacy as one of its active members sees it: their own membership and what it allows them.</summary>
public sealed record MyPharmacyDto(
    Guid Id,
    string Name,
    Guid MembershipId,
    OrganisationMembershipRole Role,
    OrganisationJobFunction JobFunction,
    bool CanManageMembers,
    bool CanAssignCourses);
