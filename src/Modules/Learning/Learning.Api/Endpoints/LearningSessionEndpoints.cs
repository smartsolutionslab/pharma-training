using System.Security.Claims;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Api;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.Commands.ReportProgress;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.Commands.StartLearningSession;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.DTOs;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.Queries.GetLearningSession;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.LearningSessions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Api.Endpoints;

/// <summary>
/// A learner's sessions (stories L1 and L2): start or resume one on a course version, read one's own and report progress
/// on it. Only an active member of any pharmacy, by Learning's copy of the memberships, may start or resume one; a former
/// member still reads their own (docs/adr/0017). The handlers check it, since a membership is no realm role
/// (docs/adr/0003); reporting progress does not check it yet (#242). The learner is always the token's subject, never
/// the body.
/// </summary>
public sealed class LearningSessionEndpoints : IEndpoint
{
    public void Map(IEndpointRouteBuilder endpoints)
    {
        Ensure.That(endpoints).IsNotNull();

        // 201 with the session's location when this call started it, 200 when it resumed one not finished yet.
        endpoints.MapPost("/sessions", StartAsync).RequireAuthorization();
        endpoints.MapGet("/sessions/{id:guid}", ReadAsync).RequireAuthorization();

        // 200 with the session as stored, whether the report added progress or was older or repeated.
        endpoints.MapPost("/sessions/{id:guid}/progress", ReportProgressAsync).RequireAuthorization();
    }

    private static async Task<Results<Created<LearningSessionDto>, Ok<LearningSessionDto>, ProblemHttpResult>> StartAsync(
        StartLearningSessionRequest request,
        ClaimsPrincipal user,
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        var learner = LearningCaller.PersonOf(user);
        if (!learner.HasValue)
        {
            return Refused(LearningSessionFailures.IdentityIncomplete());
        }

        // The empty Guid is no version Learning holds; it is not found like any other.
        if (request.CourseVersionId == Guid.Empty)
        {
            return Refused(LearningSessionFailures.CourseVersionNotFound(request.CourseVersionId));
        }

        var started = await dispatcher.SendAsync(new StartLearningSession(learner.Value, CourseVersionId.From(request.CourseVersionId)), cancellationToken);
        if (started.IsFailure)
        {
            return Refused(started.Error);
        }

        return (await dispatcher.QueryAsync(new GetLearningSession(started.Value.Id, learner.Value.Value), cancellationToken))
            .ToCreatedOrOkHttpResult(started.Value.Created, Location);
    }

    private static async Task<Results<Ok<LearningSessionDto>, ProblemHttpResult>> ReadAsync(
        Guid id,
        ClaimsPrincipal user,
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        var learner = LearningCaller.PersonOf(user);
        if (!learner.HasValue)
        {
            return Result<LearningSessionDto, ApiError>.Failure(LearningSessionFailures.IdentityIncomplete()).ToHttpResult();
        }

        return (await dispatcher.QueryAsync(new GetLearningSession(id, learner.Value.Value), cancellationToken)).ToHttpResult();
    }

    private static async Task<Results<Ok<LearningSessionDto>, ProblemHttpResult>> ReportProgressAsync(
        Guid id,
        ReportLearningProgressRequest request,
        ClaimsPrincipal user,
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        var learner = LearningCaller.PersonOf(user);
        if (!learner.HasValue)
        {
            return Result<LearningSessionDto, ApiError>.Failure(LearningSessionFailures.IdentityIncomplete()).ToHttpResult();
        }

        // The empty Guid is no session; it is not found like any other.
        if (id == Guid.Empty)
        {
            return Result<LearningSessionDto, ApiError>.Failure(LearningSessionFailures.SessionNotFound(id)).ToHttpResult();
        }

        // A missing percent is a failed precondition like one out of range: 400 invalid-argument, never a silent 0.
        Ensure.That(request.Percent.GetValueOrDefault(), "percent").Satisfies(_ => request.Percent.HasValue, "percent is required.");
        var report = new ReportProgress(
            LearningSessionId.From(id),
            learner.Value,
            ChapterId.From(request.ChapterId),
            ProgressPercent.From(request.Percent.GetValueOrDefault()));
        var reported = await dispatcher.SendAsync(report, cancellationToken);
        if (reported.IsFailure)
        {
            return Result<LearningSessionDto, ApiError>.Failure(reported.Error).ToHttpResult();
        }

        return (await dispatcher.QueryAsync(new GetLearningSession(id, learner.Value.Value), cancellationToken)).ToHttpResult();
    }

    private static Results<Created<LearningSessionDto>, Ok<LearningSessionDto>, ProblemHttpResult> Refused(ApiError error) =>
        Result<LearningSessionDto, ApiError>.Failure(error).ToCreatedOrOkHttpResult(created: false, Location);

    private static string Location(LearningSessionDto session) => $"/learning/sessions/{session.Id}";
}
