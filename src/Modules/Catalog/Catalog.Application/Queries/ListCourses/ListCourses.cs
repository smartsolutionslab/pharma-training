using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Catalog.Application.DTOs;

namespace SmartSolutionsLab.PharmaTraining.Modules.Catalog.Application.Queries.ListCourses;

/// <summary>One page of the courses as their current version, by title; a course whose current version is retired is not listed.</summary>
public sealed record ListCourses(Page Page, PageSize PageSize) : IQuery<Result<PagedResult<CatalogCourseDto>, ApiError>>;
