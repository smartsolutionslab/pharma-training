using System.Diagnostics;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Api;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.AddFileChapter;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.AddTextChapter;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.RemoveChapter;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.ReorderChapters;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.ReplaceFileChapter;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.ReplaceTextChapter;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Api.Endpoints;

/// <summary>
/// An editor adds, replaces, removes and reorders the chapters of a draft: Markdown text, or a PDF or video uploaded
/// through a URL the uploads endpoint issued. What the caller got wrong is
/// refused before it becomes a value object, whose factory would throw on it; the empty Guid is no version or chapter.
/// </summary>
public sealed class ChapterEndpoints : IEndpoint
{
    public void Map(IEndpointRouteBuilder endpoints)
    {
        Ensure.That(endpoints).IsNotNull();

        endpoints.MapPost("/course-versions/{id:guid}/chapters", async (Guid id, EditorialChapterRequest request, IDispatcher dispatcher, CancellationToken cancellationToken) =>
        {
            // A chapter has no resource of its own: its location is the version that holds it.
            string Location(Guid chapter) => CourseVersionEndpoints.Location(id);

            if (id == Guid.Empty)
            {
                return Result<Guid, ApiError>.Failure(CourseVersionFailures.NotFound(id)).ToCreatedHttpResult(Location);
            }

            var parsed = Content(request);
            if (parsed.IsFailure)
            {
                return Result<Guid, ApiError>.Failure(parsed.Error).ToCreatedHttpResult(Location);
            }

            var content = parsed.Value;
            var version = CourseVersionId.From(id);
            if (content.File is { } file)
            {
                var addFile = new AddFileChapter(version, content.Title, content.Kind, file, content.Duration);
                return (await dispatcher.SendAsync(addFile, cancellationToken)).ToCreatedHttpResult(Location);
            }

            var addText = new AddTextChapter(version, content.Title, content.Text ?? throw new UnreachableException("A text chapter has its text."));
            return (await dispatcher.SendAsync(addText, cancellationToken)).ToCreatedHttpResult(Location);
        }).RequireAuthorization(EditorialPolicyNames.Edit);

        endpoints.MapPut("/course-versions/{id:guid}/chapters/{chapterId:guid}", async (Guid id, Guid chapterId, EditorialChapterRequest request, IDispatcher dispatcher, CancellationToken cancellationToken) =>
        {
            if (Unknown(id, chapterId) is { HasValue: true } unknown)
            {
                return Result<Unit, ApiError>.Failure(unknown.Value).ToHttpResult();
            }

            var parsed = Content(request);
            if (parsed.IsFailure)
            {
                return Result<Unit, ApiError>.Failure(parsed.Error).ToHttpResult();
            }

            var content = parsed.Value;
            var version = CourseVersionId.From(id);
            var chapter = ChapterId.From(chapterId);
            if (content.File is { } file)
            {
                var replaceFile = new ReplaceFileChapter(version, chapter, content.Title, content.Kind, file, content.Duration);
                return (await dispatcher.SendAsync(replaceFile, cancellationToken)).ToHttpResult();
            }

            var replaceText = new ReplaceTextChapter(version, chapter, content.Title, content.Text ?? throw new UnreachableException("A text chapter has its text."));
            return (await dispatcher.SendAsync(replaceText, cancellationToken)).ToHttpResult();
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

    /// <summary>
    /// The chapter the body describes, checked in order, the first failure answered: its title, its kind (Text when it
    /// names none), no field its kind does not take, then the text of a text chapter, or the file of a PDF or video and
    /// a video's duration. Whether the file was uploaded for this version is the command's to check.
    /// </summary>
    private static Result<ChapterContent, ApiError> Content(EditorialChapterRequest request)
    {
        if (!ChapterTitle.TryFrom(request.Title, out var title))
        {
            return Failure(CourseVersionFailures.InvalidChapterTitle());
        }

        // The enum's converter also reads numbers, so an undefined one is the caller's mistake, not a bug.
        var kind = request.Kind ?? EditorialChapterRequestKind.Text;
        if (!Enum.IsDefined(kind))
        {
            return Failure(CourseVersionFailures.InvalidChapterKind());
        }

        var takesText = kind == EditorialChapterRequestKind.Text;
        var takesDuration = kind == EditorialChapterRequestKind.Video;
        if ((request.Text is not null && !takesText) || (request.File is not null && takesText) || (request.DurationSeconds is not null && !takesDuration))
        {
            return Failure(CourseVersionFailures.ChapterContentDoesNotMatchKind());
        }

        if (takesText)
        {
            if (!MarkdownText.TryFrom(request.Text, out var text))
            {
                return Failure(CourseVersionFailures.InvalidChapterText());
            }

            return Success(new ChapterContent(title, ChapterKind.Text, text, null, null));
        }

        if (!BlobReference.TryFrom(request.File, out var file) || !IsBlobName(file.Value))
        {
            return Failure(CourseVersionFailures.InvalidChapterFile());
        }

        VideoDuration? duration = null;
        if (takesDuration && !VideoDuration.TryFrom(request.DurationSeconds, out duration))
        {
            return Failure(CourseVersionFailures.InvalidVideoDuration());
        }

        return Success(new ChapterContent(title, kind.ToDomain(), null, file, duration));
    }

    /// <summary>A name in the module's container: no backslash, and no empty, <c>.</c> or <c>..</c> segment (as <c>BlobName</c> requires).</summary>
    private static bool IsBlobName(string name) =>
        !name.Contains('\\', StringComparison.Ordinal) && name.Split('/').All(segment => segment.Length > 0 && segment != "." && segment != "..");

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

    /// <summary>A chapter as the body describes it: its text for a text chapter, its file and a video's duration otherwise.</summary>
    private sealed record ChapterContent(ChapterTitle Title, ChapterKind Kind, MarkdownText? Text, BlobReference? File, VideoDuration? Duration);
}
