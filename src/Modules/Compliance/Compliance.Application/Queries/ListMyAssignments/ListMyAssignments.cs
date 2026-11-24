using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.DTOs;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Members;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.Queries.ListMyAssignments;

/// <summary>
/// The learner's own open and in-progress assignments in the pharmacy, a page at a time, overdue first, each with the
/// course version to open; asked by the learner, who must be a member there, whatever their role.
/// </summary>
public sealed record ListMyAssignments(PersonId Learner, PharmacyId Pharmacy, Page Page, PageSize PageSize)
    : IQuery<Result<PagedResult<ComplianceMyAssignmentDto>, ApiError>>;
