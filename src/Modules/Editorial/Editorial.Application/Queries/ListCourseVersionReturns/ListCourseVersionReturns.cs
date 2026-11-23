using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.DTOs;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Queries.ListCourseVersionReturns;

/// <summary>One page of a course version's returns, newest first.</summary>
public sealed record ListCourseVersionReturns(Guid Version, Page Page, PageSize PageSize) : IQuery<Result<PagedResult<EditorialCourseVersionReturnDto>, ApiError>>;
