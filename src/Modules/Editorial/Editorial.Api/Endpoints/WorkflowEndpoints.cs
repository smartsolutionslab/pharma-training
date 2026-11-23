using System.Security.Claims;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Api;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.PublishCourseVersion;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.RetireCourseVersion;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.ReturnToDraft;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.SubmitForReview;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Api.Endpoints;

/// <summary>
/// The editorial workflow: an editor submits a draft for review, returns it to draft, publishes it and retires it.
/// Each step is a body-less POST on the version, taken by an editor whose token names a person; submit, publish and
/// retire record the caller. The empty Guid is no version.
/// </summary>
public sealed class WorkflowEndpoints : IEndpoint
{
    public void Map(IEndpointRouteBuilder endpoints)
    {
        Ensure.That(endpoints).IsNotNull();

        endpoints.MapPost("/course-versions/{id:guid}/submit", (Guid id, ClaimsPrincipal user, IDispatcher dispatcher, CancellationToken cancellationToken) =>
            ByEditorAsync(id, user, editor => dispatcher.SendAsync(new SubmitForReview(CourseVersionId.From(id), editor), cancellationToken)))
            .RequireAuthorization(EditorialPolicyNames.Edit);

        endpoints.MapPost("/course-versions/{id:guid}/return", (Guid id, ClaimsPrincipal user, IDispatcher dispatcher, CancellationToken cancellationToken) =>
            ByEditorAsync(id, user, editor => dispatcher.SendAsync(new ReturnToDraft(CourseVersionId.From(id), editor), cancellationToken)))
            .RequireAuthorization(EditorialPolicyNames.Edit);

        endpoints.MapPost("/course-versions/{id:guid}/publish", (Guid id, ClaimsPrincipal user, IDispatcher dispatcher, CancellationToken cancellationToken) =>
            ByEditorAsync(id, user, editor => dispatcher.SendAsync(new PublishCourseVersion(CourseVersionId.From(id), editor), cancellationToken)))
            .RequireAuthorization(EditorialPolicyNames.Edit);

        endpoints.MapPost("/course-versions/{id:guid}/retire", (Guid id, ClaimsPrincipal user, IDispatcher dispatcher, CancellationToken cancellationToken) =>
            ByEditorAsync(id, user, editor => dispatcher.SendAsync(new RetireCourseVersion(CourseVersionId.From(id), editor), cancellationToken)))
            .RequireAuthorization(EditorialPolicyNames.Edit);
    }

    /// <summary>A step the calling editor takes: the empty Guid is not found, a caller without a person is refused.</summary>
    private static async Task<Results<Ok<Unit>, ProblemHttpResult>> ByEditorAsync(Guid id, ClaimsPrincipal user, Func<PersonId, Task<Result<Unit, ApiError>>> step)
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

        return (await step(editor.Value)).ToHttpResult();
    }
}
