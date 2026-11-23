using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.DTOs;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.Queries.ListPharmacyInvitations;

/// <summary>One page of a pharmacy's invitations still Invited, open or run out, by expiry, for a member who may manage its members.</summary>
public sealed record ListPharmacyInvitations(PharmacyId Pharmacy, PersonId By, Page Page, PageSize PageSize)
    : IQuery<Result<PagedResult<PharmacyInvitationDto>, ApiError>>;
