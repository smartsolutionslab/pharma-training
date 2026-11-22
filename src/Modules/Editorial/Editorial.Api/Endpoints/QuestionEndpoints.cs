using System.Security.Claims;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Api;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.AddQuestion;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.ApproveQuestion;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.EditQuestion;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.RemoveQuestion;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.DTOs;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Api.Endpoints;

/// <summary>
/// An editor adds, edits, removes and approves the questions of a draft; the approver is the caller. What the caller
/// got wrong is refused before it becomes a value object, whose factory would throw on it; the empty Guid is no
/// version, question or chapter.
/// </summary>
public sealed class QuestionEndpoints : IEndpoint
{
    public void Map(IEndpointRouteBuilder endpoints)
    {
        Ensure.That(endpoints).IsNotNull();

        endpoints.MapPost("/course-versions/{id:guid}/questions", async (Guid id, EditorialQuestionRequest request, IDispatcher dispatcher, CancellationToken cancellationToken) =>
        {
            // A question has no resource of its own: its location is the version that holds it.
            string Location(Guid question) => CourseVersionEndpoints.Location(id);

            if (id == Guid.Empty)
            {
                return Result<Guid, ApiError>.Failure(CourseVersionFailures.NotFound(id)).ToCreatedHttpResult(Location);
            }

            var drafted = Drafted(id, request);
            if (drafted.IsFailure)
            {
                return Result<Guid, ApiError>.Failure(drafted.Error).ToCreatedHttpResult(Location);
            }

            return (await dispatcher.SendAsync(new AddQuestion(CourseVersionId.From(id), drafted.Value), cancellationToken)).ToCreatedHttpResult(Location);
        }).RequireAuthorization(EditorialPolicyNames.Edit);

        endpoints.MapPut("/course-versions/{id:guid}/questions/{questionId:guid}", async (Guid id, Guid questionId, EditorialQuestionRequest request, IDispatcher dispatcher, CancellationToken cancellationToken) =>
        {
            if (Unknown(id, questionId) is { HasValue: true } unknown)
            {
                return Result<Unit, ApiError>.Failure(unknown.Value).ToHttpResult();
            }

            var drafted = Drafted(id, request);
            if (drafted.IsFailure)
            {
                return Result<Unit, ApiError>.Failure(drafted.Error).ToHttpResult();
            }

            var command = new EditQuestion(CourseVersionId.From(id), QuestionId.From(questionId), drafted.Value);
            return (await dispatcher.SendAsync(command, cancellationToken)).ToHttpResult();
        }).RequireAuthorization(EditorialPolicyNames.Edit);

        endpoints.MapDelete("/course-versions/{id:guid}/questions/{questionId:guid}", async (Guid id, Guid questionId, IDispatcher dispatcher, CancellationToken cancellationToken) =>
        {
            if (Unknown(id, questionId) is { HasValue: true } unknown)
            {
                return Result<Unit, ApiError>.Failure(unknown.Value).ToHttpResult();
            }

            return (await dispatcher.SendAsync(new RemoveQuestion(CourseVersionId.From(id), QuestionId.From(questionId)), cancellationToken)).ToHttpResult();
        }).RequireAuthorization(EditorialPolicyNames.Edit);

        endpoints.MapPost("/course-versions/{id:guid}/questions/{questionId:guid}/approve", async (Guid id, Guid questionId, ClaimsPrincipal user, IDispatcher dispatcher, CancellationToken cancellationToken) =>
        {
            if (Unknown(id, questionId) is { HasValue: true } unknown)
            {
                return Result<Unit, ApiError>.Failure(unknown.Value).ToHttpResult();
            }

            var approver = EditorialCaller.PersonOf(user);
            if (!approver.HasValue)
            {
                return Result<Unit, ApiError>.Failure(CourseVersionFailures.IdentityIncomplete()).ToHttpResult();
            }

            var command = new ApproveQuestion(CourseVersionId.From(id), QuestionId.From(questionId), approver.Value);
            return (await dispatcher.SendAsync(command, cancellationToken)).ToHttpResult();
        }).RequireAuthorization(EditorialPolicyNames.Edit);
    }

    /// <summary>The empty Guid is no version's and no question's id: not found, before anything is loaded.</summary>
    private static Option<ApiError> Unknown(Guid id, Guid questionId)
    {
        if (id == Guid.Empty)
        {
            return Option<ApiError>.Some(CourseVersionFailures.NotFound(id));
        }

        if (questionId == Guid.Empty)
        {
            return Option<ApiError>.Some(CourseVersionFailures.UnknownQuestion(id, questionId));
        }

        return Option<ApiError>.None;
    }

    /// <summary>
    /// The question the caller sent, as values, or what the caller got wrong in it. The binder leaves a property the
    /// body lacks null or 0, whatever the record says: a missing chapter is the empty Guid, a missing type is no type.
    /// </summary>
    private static Result<DraftedQuestion, ApiError> Drafted(Guid id, EditorialQuestionRequest request)
    {
        if (request.ChapterId == Guid.Empty)
        {
            return Failure(CourseVersionFailures.UnknownChapter(id, request.ChapterId));
        }

        if (!QuestionText.TryFrom(request.Text, out var text))
        {
            return Failure(CourseVersionFailures.InvalidQuestionText());
        }

        if (!Enum.IsDefined(request.Type))
        {
            return Failure(CourseVersionFailures.InvalidQuestionType());
        }

        if (request.Options is not { } options || options.Any(option => option is null))
        {
            return Failure(CourseVersionFailures.InvalidAnswerOptions());
        }

        List<(AnswerText Text, Correctness Correctness)> drafted = [];
        foreach (var option in options)
        {
            if (!AnswerText.TryFrom(option.Text, out var answer))
            {
                return Failure(CourseVersionFailures.InvalidAnswerText());
            }

            drafted.Add((answer, option.Correct ? Correctness.Correct : Correctness.Incorrect));
        }

        return Success(new DraftedQuestion(text, request.Type.ToDomain(), ChapterId.From(request.ChapterId), drafted));
    }
}
