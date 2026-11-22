using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Catalog.Application.DTOs;

namespace SmartSolutionsLab.PharmaTraining.Modules.Catalog.Application.Queries.GetCourseVersion;

/// <summary>One course version by its id, retired or superseded too; the empty Guid is no version's.</summary>
public sealed record GetCourseVersion(Guid Id) : IQuery<Result<CatalogCourseVersionDto, ApiError>>;
