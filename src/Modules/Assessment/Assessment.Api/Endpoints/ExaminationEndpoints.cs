using System.Security.Claims;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Api;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.Commands.AnswerQuestion;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.Commands.StartAttempt;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.Commands.SubmitAttempt;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.DTOs;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.Queries.GetAttemptResult;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.Queries.GetCurrentAttempt;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.Queries.GetExaminationBySession;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.Queries.GetExaminationSession;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Api.Endpoints;

/// <summary>
/// A learner's examination (story A1): read by its learning session, then an attempt started, answered and submitted
/// by the examination's id. Open to every signed-in person; the learner is always the token's subject, never the body,
/// and another person's examination is not found, since examination ids are derivable. Nothing answered here says
/// which option is right.
/// </summary>
public sealed class ExaminationEndpoints : IEndpoint
{
    public void Map(IEndpointRouteBuilder endpoints)
    {
        Ensure.That(endpoints).IsNotNull();

        endpoints.MapGet("/examinations/by-session/{sessionId:guid}", ReadBySessionAsync).RequireAuthorization();

        // 201 with the attempt; its location is the examination read by session, which holds it as the current attempt.
        endpoints.MapPost("/examinations/{id:guid}/attempts", StartAsync).RequireAuthorization();
        endpoints.MapPut("/examinations/{id:guid}/attempts/current/answers/{questionId:guid}", AnswerAsync).RequireAuthorization();
        endpoints.MapPost("/examinations/{id:guid}/attempts/current/submit", SubmitAsync).RequireAuthorization();
    }

    private static async Task<Results<Ok<AssessmentExaminationDto>, ProblemHttpResult>> ReadBySessionAsync(
        Guid sessionId,
        ClaimsPrincipal user,
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        var learner = AssessmentCaller.PersonOf(user);
        if (!learner.HasValue)
        {
            return Result<AssessmentExaminationDto, ApiError>.Failure(ExaminationFailures.IdentityIncomplete()).ToHttpResult();
        }

        // The empty Guid is no session; it is not found like any other.
        if (sessionId == Guid.Empty)
        {
            return Result<AssessmentExaminationDto, ApiError>.Failure(ExaminationFailures.NotFound(sessionId)).ToHttpResult();
        }

        return (await dispatcher.QueryAsync(new GetExaminationBySession(sessionId, learner.Value.Value), cancellationToken)).ToHttpResult();
    }

    private static async Task<Results<Created<AssessmentAttemptDto>, ProblemHttpResult>> StartAsync(
        Guid id,
        ClaimsPrincipal user,
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        var learner = AssessmentCaller.PersonOf(user);
        if (!learner.HasValue)
        {
            return Refused(ExaminationFailures.IdentityIncomplete());
        }

        if (id == Guid.Empty)
        {
            return Refused(ExaminationFailures.NotFound(id));
        }

        var started = await dispatcher.SendAsync(new StartAttempt(ExaminationId.From(id), learner.Value), cancellationToken);
        if (started.IsFailure)
        {
            return Refused(started.Error);
        }

        var session = await dispatcher.QueryAsync(new GetExaminationSession(id, learner.Value.Value), cancellationToken);
        if (session.IsFailure)
        {
            return Refused(session.Error);
        }

        return (await dispatcher.QueryAsync(new GetCurrentAttempt(id, learner.Value.Value), cancellationToken))
            .ToCreatedHttpResult(_ => $"/assessment/examinations/by-session/{session.Value}");
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> AnswerAsync(
        Guid id,
        Guid questionId,
        AnswerAssessmentQuestionRequest request,
        ClaimsPrincipal user,
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        var learner = AssessmentCaller.PersonOf(user);
        if (!learner.HasValue)
        {
            return Result<Unit, ApiError>.Failure(ExaminationFailures.IdentityIncomplete()).ToNoContentHttpResult();
        }

        if (id == Guid.Empty)
        {
            return Result<Unit, ApiError>.Failure(ExaminationFailures.NotFound(id)).ToNoContentHttpResult();
        }

        // An empty answer is refused rather than taken as clearing it: an unanswered question already counts as wrong.
        Ensure.That(request).IsNotNull();
        Ensure.That(request.OptionIds, "optionIds").IsNotNull();
        Ensure.That(request.OptionIds.Count, "optionIds").AtLeast(1);
        Ensure.That(request.OptionIds.Distinct().Count(), "optionIds").Satisfies(distinct => distinct == request.OptionIds.Count, "each option is chosen once.");
        var answer = new AnswerQuestion(
            ExaminationId.From(id),
            learner.Value,
            QuestionVersionId.From(questionId),
            [.. request.OptionIds.Select(AnswerOptionId.From)]);
        return (await dispatcher.SendAsync(answer, cancellationToken)).ToNoContentHttpResult();
    }

    private static async Task<Results<Ok<AssessmentAttemptResultDto>, ProblemHttpResult>> SubmitAsync(
        Guid id,
        ClaimsPrincipal user,
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        var learner = AssessmentCaller.PersonOf(user);
        if (!learner.HasValue)
        {
            return Result<AssessmentAttemptResultDto, ApiError>.Failure(ExaminationFailures.IdentityIncomplete()).ToHttpResult();
        }

        if (id == Guid.Empty)
        {
            return Result<AssessmentAttemptResultDto, ApiError>.Failure(ExaminationFailures.NotFound(id)).ToHttpResult();
        }

        var submitted = await dispatcher.SendAsync(new SubmitAttempt(ExaminationId.From(id), learner.Value), cancellationToken);
        if (submitted.IsFailure)
        {
            return Result<AssessmentAttemptResultDto, ApiError>.Failure(submitted.Error).ToHttpResult();
        }

        return (await dispatcher.QueryAsync(new GetAttemptResult(id, submitted.Value.Value, learner.Value.Value), cancellationToken)).ToHttpResult();
    }

    private static Results<Created<AssessmentAttemptDto>, ProblemHttpResult> Refused(ApiError error) =>
        Result<AssessmentAttemptDto, ApiError>.Failure(error).ToCreatedHttpResult(_ => string.Empty);
}
