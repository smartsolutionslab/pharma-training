using SmartSolutionsLab.PharmaTraining.Contracts.Editorial;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.ReadModels;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.LearningSessions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.Consumers;

/// <summary>
/// Stores a course version Editorial published with its chapters, once: the chapter structure a session takes at
/// start. A version is published once, so a version already stored means its event was handled, whatever the event's
/// identifier or payload: a second delivery, or a republish, changes nothing, and the first one wins. The primary key
/// backs the check: of two deliveries racing, the loser's insert fails and its retry finds the row.
/// </summary>
public sealed class CourseVersionPublishedV1Consumer(ICourseVersionCopies versions, ILearningUnitOfWork unitOfWork)
{
    public async Task HandleAsync(CourseVersionPublishedV1 message, CancellationToken cancellationToken)
    {
        // Refused here, each by its name, so a poison message dead-letters with a reason that says which field.
        Ensure.That(message).IsNotNull();
        Ensure.That(message.CourseId, nameof(message.CourseId)).IsNotEmpty();
        Ensure.That(message.CourseVersionId, nameof(message.CourseVersionId)).IsNotEmpty();
        Ensure.That(message.Chapters, nameof(message.Chapters)).IsNotNull();
        Ensure.That(message.Chapters.Count, nameof(message.Chapters)).AtLeast(1);
        HashSet<Guid> chapterIds = [];
        foreach (var chapter in message.Chapters)
        {
            Ensure.That(chapter, nameof(message.Chapters)).IsNotNull();
            Ensure.That(chapter.ChapterId, nameof(chapter.ChapterId)).IsNotEmpty();
            Ensure.That(chapter.ChapterId.ToString(), nameof(chapter.ChapterId)).Satisfies(_ => chapterIds.Add(chapter.ChapterId), "A chapter id may appear once in a version.");
            Ensure.That(chapter.Position, nameof(chapter.Position)).AtLeast(1);
        }

        var version = CourseVersionId.From(message.CourseVersionId);
        if (await versions.ExistsAsync(version, cancellationToken))
        {
            return;
        }

        versions.Add(new CourseVersionCopy
        {
            Id = message.CourseVersionId,
            CourseId = message.CourseId,
            VersionNumber = message.VersionNumber,
            Chapters = [.. message.Chapters.Select(chapter => new CourseVersionChapterCopy
            {
                CourseVersionId = message.CourseVersionId,
                ChapterId = chapter.ChapterId,
                Position = chapter.Position,
                Kind = KindOf(chapter.Kind),
                VideoDurationSeconds = chapter.VideoDurationSeconds,
            })],
        });
        await unitOfWork.CommitAsync(cancellationToken);
    }

    private static ChapterKind KindOf(ChapterKindV1 kind) => kind switch
    {
        ChapterKindV1.Text => ChapterKind.Text,
        ChapterKindV1.Pdf => ChapterKind.Pdf,
        ChapterKindV1.Video => ChapterKind.Video,
        _ => throw new InvalidOperationException($"Chapter kind {kind} has no counterpart in Learning."),
    };
}
