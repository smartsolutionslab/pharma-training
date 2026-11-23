using System.Security.Claims;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Api;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.DTOs;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.Queries.GetChapterMedia;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Api.Endpoints;

/// <summary>
/// A chapter's PDF or video for the learner (#41): a fifteen-minute read URL on Learning's own copy, for a caller with a
/// session on a version that holds the chapter. Open to every signed-in person, like the sessions; the learner is always
/// the token's subject.
/// </summary>
public sealed class LearningMediaEndpoints : IEndpoint
{
    public void Map(IEndpointRouteBuilder endpoints)
    {
        Ensure.That(endpoints).IsNotNull();

        endpoints.MapGet("/media/{chapterId:guid}", ReadAsync).RequireAuthorization();
    }

    private static async Task<Results<Ok<LearningMediaUrlDto>, ProblemHttpResult>> ReadAsync(
        Guid chapterId,
        ClaimsPrincipal user,
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        var learner = LearningCaller.PersonOf(user);
        if (!learner.HasValue)
        {
            return Result<LearningMediaUrlDto, ApiError>.Failure(LearningSessionFailures.IdentityIncomplete()).ToHttpResult();
        }

        return (await dispatcher.QueryAsync(new GetChapterMedia(chapterId, learner.Value.Value), cancellationToken)).ToHttpResult();
    }
}
