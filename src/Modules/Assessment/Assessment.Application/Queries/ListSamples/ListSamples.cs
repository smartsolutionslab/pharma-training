using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.DTOs;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.Queries.ListSamples;

/// <summary>One page of the module's samples, by name.</summary>
public sealed record ListSamples(Page Page, PageSize PageSize) : IQuery<Result<PagedResult<AssessmentSampleDto>, ApiError>>;
