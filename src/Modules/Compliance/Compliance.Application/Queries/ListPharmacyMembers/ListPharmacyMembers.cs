using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.DTOs;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Members;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.Queries.ListPharmacyMembers;

/// <summary>
/// One page of the pharmacy's active members for the assign wizard, one row per person, by name, optionally only those
/// whose current job function is the one given; asked by a member whose current role may assign courses there.
/// </summary>
public sealed record ListPharmacyMembers(PharmacyId Pharmacy, PersonId By, JobFunction? JobFunction, Page Page, PageSize PageSize)
    : IQuery<Result<PagedResult<ComplianceMemberDto>, ApiError>>;
