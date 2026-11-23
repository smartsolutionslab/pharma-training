using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.DTOs;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.Queries.ListPharmacyMembers;

/// <summary>One page of a pharmacy's active members, by name, for a member who may manage its members.</summary>
public sealed record ListPharmacyMembers(PharmacyId Pharmacy, PersonId By, Page Page, PageSize PageSize)
    : IQuery<Result<PagedResult<PharmacyMemberDto>, ApiError>>;
