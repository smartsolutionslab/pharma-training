using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Api;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.AddTextChapter;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.RemoveChapter;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.ReorderChapters;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.ReplaceTextChapter;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Api.Endpoints;

/// <summary>
/// An editor adds, replaces, removes and reorders the Markdown chapters of a draft. What the caller got wrong is
/// refused before it becomes a value object, whose factory would throw on it; the empty Guid is no version or chapter.
/// </summary>
public sealed class ChapterEndpoints : IEndpoint
{
    public void Map(IEndpointRouteBuilder endpoints)
    {
        Ensure.That(endpoints).IsNotNull();

        endpoints.MapPost("/course-versions/{id:guid}/chapters", async (Guid id, EditorialTextChapterRequest request, IDispatcher dispatcher, CancellationToken cancellationToken) =>
        {
            // A chapter has no resource of its own: its location is the version that holds it.
            string Location(Guid chapter) => CourseVersionEndpoints.Location(id);

            if (id == Guid.Empty)
            {
                return Result<Guid, ApiError>.Failure(CourseVersionFailures.NotFound(id)).ToCreatedHttpResult(Location);
            }

            if (!ChapterTitle.TryFrom(request.Title, out var title))
            {
                return Result<Guid, ApiError>.Failure(CourseVersionFailures.InvalidChapterTitle()).ToCreatedHttpResult(Location);
            }

            if (!MarkdownText.TryFrom(request.Text, out var text))
            {
                return Result<Guid, ApiError>.Failure(CourseVersionFailures.InvalidChapterText()).ToCreatedHttpResult(Location);
            }

            return (await dispatcher.SendAsync(new AddTextChapter(CourseVersionId.From(id), title, text), cancellationToken)).ToCreatedHttpResult(Location);
        }).RequireAuthorization(EditorialPolicyNames.Edit);

        endpoints.MapPut("/course-versions/{id:guid}/chapters/{chapterId:guid}", async (Guid id, Guid chapterId, EditorialTextChapterRequest request, IDispatcher dispatcher, CancellationToken cancellationToken) =>
        {
            if (Unknown(id, chapterId) is { HasValue: true } unknown)
            {
                return Result<Unit, ApiError>.Failure(unknown.Value).ToHttpResult();
            }

            if (!ChapterTitle.TryFrom(request.Title, out var title))
            {
                return Result<Unit, ApiError>.Failure(CourseVersionFailures.InvalidChapterTitle()).ToHttpResult();
            }

            if (!MarkdownText.TryFrom(request.Text, out var text))
            {
                return Result<Unit, ApiError>.Failure(CourseVersionFailures.InvalidChapterText()).ToHttpResult();
            }

            var command = new ReplaceTextChapter(CourseVersionId.From(id), ChapterId.From(chapterId), title, text);
            return (await dispatcher.SendAsync(command, cancellationToken)).ToHttpResult();
        }).RequireAuthorization(EditorialPolicyNames.Edit);

        endpoints.MapDelete("/course-versions/{id:guid}/chapters/{chapterId:guid}", async (Guid id, Guid chapterId, IDispatcher dispatcher, CancellationToken cancellationToken) =>
        {
            if (Unknown(id, chapterId) is { HasValue: true } unknown)
            {
                return Result<Unit, ApiError>.Failure(unknown.Value).ToHttpResult();
            }

            return (await dispatcher.SendAsync(new RemoveChapter(CourseVersionId.From(id), ChapterId.From(chapterId)), cancellationToken)).ToHttpResult();
        }).RequireAuthorization(EditorialPolicyNames.Edit);

        endpoints.MapPut("/course-versions/{id:guid}/chapters/order", async (Guid id, ReorderEditorialChaptersRequest request, IDispatcher dispatcher, CancellationToken cancellationToken) =>
        {
            if (id == Guid.Empty)
            {
                return Result<Unit, ApiError>.Failure(CourseVersionFailures.NotFound(id)).ToHttpResult();
            }

            // The binder leaves a property the body lacks null, whatever the record says; that is no order either.
            if (request.Chapters is not { } chapters || chapters.Contains(Guid.Empty))
            {
                return Result<Unit, ApiError>.Failure(CourseVersionFailures.InvalidChapterOrder()).ToHttpResult();
            }

            var command = new ReorderChapters(CourseVersionId.From(id), [.. chapters.Select(ChapterId.From)]);
            return (await dispatcher.SendAsync(command, cancellationToken)).ToHttpResult();
        }).RequireAuthorization(EditorialPolicyNames.Edit);
    }

    /// <summary>The empty Guid is no version's and no chapter's id: not found, before anything is loaded.</summary>
    private static Option<ApiError> Unknown(Guid id, Guid chapterId)
    {
        if (id == Guid.Empty)
        {
            return Option<ApiError>.Some(CourseVersionFailures.NotFound(id));
        }

        if (chapterId == Guid.Empty)
        {
            return Option<ApiError>.Some(CourseVersionFailures.UnknownChapter(id, chapterId));
        }

        return Option<ApiError>.None;
    }
}
