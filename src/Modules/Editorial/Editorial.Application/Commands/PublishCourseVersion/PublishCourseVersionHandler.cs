using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Storage;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.IntegrationEvents;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.PublishCourseVersion;

/// <summary>
/// Publishes the version and supersedes the course's published versions in one save (ADR 0007), so a course never
/// keeps two: a predecessor changed by another save since it was loaded makes the commit lose its race, and nothing
/// is saved. Only a publish that succeeds supersedes anything. In the same save it announces the version
/// (<c>CourseVersionPublishedV1</c>, without questions) and its test (<c>TestDefinitionPublishedV1</c>, with the
/// solutions, for Assessment only) through the unit of work, so a refused or lost publish sends nothing (ADR 0002).
/// When the version has PDF or video chapters, it also signs a fifteen-minute inline read URL on each file and releases
/// them in the same save (<c>ChapterFilesReleasedV1</c>), for Learning to copy into its own container (#41, ADR 0002); a
/// refused publish signs nothing.
/// </summary>
public sealed class PublishCourseVersionHandler(ICourseVersionStore versions, IEditorialUnitOfWork unitOfWork, IBlobUrls urls, ReviewSettings review, TimeProvider time)
    : ICommandHandler<PublishCourseVersion, Result<Unit, ApiError>>
{
    public async Task<Result<Unit, ApiError>> HandleAsync(PublishCourseVersion command, CancellationToken cancellationToken)
    {
        Ensure.That(command).IsNotNull();

        var found = await versions.FindForUpdateAsync(command.Version, cancellationToken);
        if (!found.HasValue)
        {
            return Failure(CourseVersionFailures.NotFound(command.Version.Value));
        }

        var version = found.Value;
        var predecessors = await versions.ListPublishedForUpdateAsync(version.Course, cancellationToken);

        var published = version.Publish(command.Editor, review.Policy, Timestamp.From(time.GetUtcNow()));
        if (published.IsFailure)
        {
            return Failure(CourseVersionFailures.From(published.Error));
        }

        foreach (var predecessor in predecessors.Where(predecessor => predecessor.Id != version.Id))
        {
            predecessor.Supersede();
        }

        await unitOfWork.PublishAsync(version.ToCourseVersionPublishedV1());
        await unitOfWork.PublishAsync(version.ToTestDefinitionPublishedV1());

        var readUrls = await SignFilesAsync(version, cancellationToken);
        if (readUrls.Count > 0)
        {
            await unitOfWork.PublishAsync(version.ToChapterFilesReleasedV1(readUrls));
        }

        if (!await unitOfWork.TryCommitAsync(cancellationToken))
        {
            return Failure(CourseVersionFailures.Changed(command.Version.Value));
        }

        return Success(Unit.Value);
    }

    /// <summary>A read URL on each PDF and video chapter's file, for as long as a URL may live, shown inline with the kind's type.</summary>
    private async Task<Dictionary<ChapterId, BlobUrl>> SignFilesAsync(CourseVersion version, CancellationToken cancellationToken)
    {
        var lifetime = BlobUrlLifetime.From(BlobUrlLifetime.Maximum);
        Dictionary<ChapterId, BlobUrl> readUrls = [];
        foreach (var chapter in version.Chapters)
        {
            // A file chapter's file is a name the module issued, so it is a blob name.
            if (chapter.Kind is ChapterKind.Pdf or ChapterKind.Video && chapter.File is { } file)
            {
                readUrls[chapter.Id] = await urls.CreateReadUrlAsync(BlobName.From(file.Value), lifetime, ChapterUploads.ReadHeadersFor(chapter.Kind), cancellationToken);
            }
        }

        return readUrls;
    }
}
