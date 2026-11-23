using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Storage;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.DTOs;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.Media;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.ReadModels;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.LearningSessions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.Queries.GetChapterMedia;

/// <summary>
/// Signs a read URL on Learning's copy of a chapter's file (#41, docs/adr/0002), checking in this order, the first that
/// fails answering: Learning holds the chapter in some version (404); the learner has a session, finished or not, on
/// one of those versions (403), of which the highest numbered is read; the chapter there is a PDF or video (404); the
/// file was copied into Learning's container (409). The URL lives fifteen minutes, so a player's range requests last
/// through ordinary playback, and is signed inline with the kind's content type, never the stored blob's.
/// </summary>
public sealed class GetChapterMediaHandler(ICourseVersionCopies versions, ILearningSessions sessions, IStoredBlobs blobs, IBlobUrls urls)
    : IQueryHandler<GetChapterMedia, Result<LearningMediaUrlDto, ApiError>>
{
    public async Task<Result<LearningMediaUrlDto, ApiError>> HandleAsync(GetChapterMedia query, CancellationToken cancellationToken)
    {
        Ensure.That(query).IsNotNull();

        // The empty Guid is no chapter Learning holds; it is not found like any other.
        if (query.ChapterId == Guid.Empty)
        {
            return Failure(LearningMediaFailures.ChapterNotFound(query.ChapterId));
        }

        var chapter = ChapterId.From(query.ChapterId);
        var placements = await versions.ListPlacementsAsync(chapter, cancellationToken);
        if (placements.Count == 0)
        {
            return Failure(LearningMediaFailures.ChapterNotFound(query.ChapterId));
        }

        var read = await NewestWithASessionAsync(placements, PersonId.From(query.Learner), cancellationToken);
        if (!read.HasValue)
        {
            return Failure(LearningMediaFailures.NoSessionOnVersion(query.ChapterId));
        }

        var placement = read.Value;
        if (placement.Kind is not (ChapterKind.Pdf or ChapterKind.Video))
        {
            return Failure(LearningMediaFailures.HasNoMedia(query.ChapterId));
        }

        var name = ChapterMedia.NameFor(CourseVersionId.From(placement.CourseVersionId), chapter, placement.Kind);
        var copied = await blobs.FindAsync(name, cancellationToken);
        if (!copied.HasValue)
        {
            return Failure(LearningMediaFailures.NotReady(query.ChapterId));
        }

        var url = await urls.CreateReadUrlAsync(name, BlobUrlLifetime.From(BlobUrlLifetime.Maximum), ChapterMedia.ReadHeadersFor(placement.Kind), cancellationToken);
        return Success(url.ToDto(placement.Kind));
    }

    /// <summary>Of the versions holding the chapter, the highest numbered the learner has a session on; ties go to the version id, ordinal.</summary>
    private async Task<Option<ChapterPlacement>> NewestWithASessionAsync(IReadOnlyList<ChapterPlacement> placements, PersonId learner, CancellationToken cancellationToken)
    {
        var ordered = placements
            .OrderByDescending(placement => placement.VersionNumber)
            .ThenBy(placement => placement.CourseVersionId.ToString("D"), StringComparer.Ordinal);
        foreach (var placement in ordered)
        {
            var runs = await sessions.ListAsync(learner, CourseVersionId.From(placement.CourseVersionId), cancellationToken);
            if (runs.Count > 0)
            {
                return Option<ChapterPlacement>.Some(placement);
            }
        }

        return Option<ChapterPlacement>.None;
    }
}
