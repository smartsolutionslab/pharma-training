using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Api;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.ApplyAiTestDraft;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.DTOs;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Api.Endpoints;

/// <summary>
/// An editor has the model draft a version's test and applies it (story E9, ADR 0008 D10). It is a POST because every
/// call asks the model again and replaces the unreviewed questions; it answers 200, not 201, because nothing
/// addressable is created: the drafted questions are read back with the version. The empty Guid is no version. Each
/// editor may ask a limited number of times a minute and a day (docs/adr/0020); over it, 429
/// editorial.model-limit-reached and the model is not asked.
/// </summary>
public sealed class TestDraftEndpoints : IEndpoint
{
    public void Map(IEndpointRouteBuilder endpoints)
    {
        Ensure.That(endpoints).IsNotNull();

        endpoints.MapPost("/course-versions/{id:guid}/test-draft", async (Guid id, IDispatcher dispatcher, CancellationToken cancellationToken) =>
        {
            if (id == Guid.Empty)
            {
                return Result<EditorialTestDraftDto, ApiError>.Failure(CourseVersionFailures.NotFound(id)).ToHttpResult();
            }

            return (await dispatcher.SendAsync(new ApplyAiTestDraft(CourseVersionId.From(id)), cancellationToken)).ToHttpResult();
        })
        .RequireAuthorization(EditorialPolicyNames.Edit)
        .RequireUserRateLimit(EditorialModelLimits.TestDraft);
    }
}
