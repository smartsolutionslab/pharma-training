using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.DTOs;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Queries.GetCourseVersion;

/// <summary>One course version with its chapters in order; the empty Guid is no version's.</summary>
public sealed record GetCourseVersion(Guid Id) : IQuery<Result<EditorialCourseVersionDto, ApiError>>;
