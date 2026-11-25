namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.DTOs;

/// <summary>
/// An erasure as support reads it back: who asked and when, whether the confirmation was mailed (false while it is due,
/// and when Organisation held no address) and when that was handled, and the modules that completed their part.
/// </summary>
public sealed record OrganisationErasureDto(
    Guid Id,
    DateTimeOffset RequestedAt,
    Guid RequestedBy,
    bool ConfirmationMailed,
    DateTimeOffset? ConfirmationHandledAt,
    IReadOnlyList<OrganisationErasureStepDto> Steps);
