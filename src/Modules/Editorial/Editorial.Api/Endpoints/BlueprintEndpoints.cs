using System.Security.Claims;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Api;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.SetTestBlueprint;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Api.Endpoints;

/// <summary>
/// An editor sets the test blueprint of a draft and is recorded as the one who changed it, so a caller whose token
/// names no person is refused (403 <c>editorial.identity-incomplete</c>) before the body is checked. What the caller
/// got wrong is refused before it becomes a value object, whose factory would throw on it; the empty Guid is no version.
/// </summary>
public sealed class BlueprintEndpoints : IEndpoint
{
    public void Map(IEndpointRouteBuilder endpoints)
    {
        Ensure.That(endpoints).IsNotNull();

        endpoints.MapPut("/course-versions/{id:guid}/blueprint", async (Guid id, EditorialBlueprintRequest request, ClaimsPrincipal user, IDispatcher dispatcher, CancellationToken cancellationToken) =>
        {
            if (id == Guid.Empty)
            {
                return Result<Unit, ApiError>.Failure(CourseVersionFailures.NotFound(id)).ToHttpResult();
            }

            var editor = EditorialCaller.PersonOf(user);
            if (!editor.HasValue)
            {
                return Result<Unit, ApiError>.Failure(CourseVersionFailures.IdentityIncomplete()).ToHttpResult();
            }

            var blueprint = Blueprint(request);
            if (blueprint.IsFailure)
            {
                return Result<Unit, ApiError>.Failure(blueprint.Error).ToHttpResult();
            }

            return (await dispatcher.SendAsync(new SetTestBlueprint(CourseVersionId.From(id), blueprint.Value, editor.Value), cancellationToken)).ToHttpResult();
        }).RequireAuthorization(EditorialPolicyNames.Edit);
    }

    /// <summary>The blueprint the caller sent, as values, or the first of its values out of range or missing, or what the blueprint refuses.</summary>
    private static Result<TestBlueprint, ApiError> Blueprint(EditorialBlueprintRequest request)
    {
        if (request.Questions is not { } questions || !QuestionCount.TryFrom(questions, out var count))
        {
            return Failure(CourseVersionFailures.InvalidQuestionCount());
        }

        if (request.PassingScorePercent is not { } passingScore || !ScorePercent.TryFrom(passingScore, out var score))
        {
            return Failure(CourseVersionFailures.InvalidPassingScore());
        }

        if (request.ValidityMonths is not { } validityMonths || !RecurrenceInterval.TryFrom(validityMonths, out var validity))
        {
            return Failure(CourseVersionFailures.InvalidValidity());
        }

        // The domain decides how few questions a test may have.
        return TestBlueprint.TryCreate(count, score, validity).MapError(CourseVersionFailures.From);
    }
}
