using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.DTOs;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Queries.ListCourseVersions;

/// <summary>One page of the course versions, of one status or of all, by title, then newest number first.</summary>
public sealed record ListCourseVersions(CourseVersionStatus? Status, Page Page, PageSize PageSize) : IQuery<Result<PagedResult<EditorialCourseVersionSummaryDto>, ApiError>>;
