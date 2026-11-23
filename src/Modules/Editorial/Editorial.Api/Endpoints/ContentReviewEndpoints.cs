using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Api;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Queries.ReviewContent;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Api.Endpoints;

/// <summary>
/// The AI content review of a course version (ADR 0009): hints per text chapter on gaps, contradictions and
/// readability. A POST because every call asks the model anew; it changes nothing. Each editor may ask a limited number
/// of times a minute and a day (docs/adr/0020); over it, 429 editorial.model-limit-reached and the model is not asked.
/// </summary>
public sealed class ContentReviewEndpoints : IEndpoint
{
    public void Map(IEndpointRouteBuilder endpoints)
    {
        Ensure.That(endpoints).IsNotNull();

        endpoints.MapPost("/course-versions/{id:guid}/content-review", async (Guid id, IDispatcher dispatcher, CancellationToken cancellationToken) =>
            (await dispatcher.QueryAsync(new ReviewContent(id), cancellationToken)).ToHttpResult())
            .RequireAuthorization(EditorialPolicyNames.Edit)
            .RequireUserRateLimit(EditorialModelLimits.ContentReview);
    }
}
