using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.ReadModels;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.LearningSessions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Tests.Sessions;

/// <summary>Local copies of published course versions, as Learning's consumer stores them.</summary>
internal static class CourseVersionCopies
{
    /// <summary>The default video length of a copy, the 420 s film the unit tests watch.</summary>
    public const int VideoSeconds = 420;

    public static CourseVersionCopy Of(Guid courseVersionId, params (Guid ChapterId, int Position, ChapterKind Kind)[] chapters) =>
        Of(courseVersionId, VideoSeconds, chapters);

    /// <summary>A copy whose video chapters last <paramref name="videoSeconds"/>; null stands for a copy that lost its video's duration.</summary>
    public static CourseVersionCopy Of(Guid courseVersionId, int? videoSeconds, params (Guid ChapterId, int Position, ChapterKind Kind)[] chapters) =>
        new()
        {
            Id = courseVersionId,
            CourseId = Guid.CreateVersion7(),
            VersionNumber = 1,
            Chapters = [.. chapters.Select(chapter => new CourseVersionChapterCopy
            {
                CourseVersionId = courseVersionId,
                ChapterId = chapter.ChapterId,
                Position = chapter.Position,
                Kind = chapter.Kind,
                VideoDurationSeconds = chapter.Kind == ChapterKind.Video ? videoSeconds : null,
            })],
        };
}
