using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.ReadModels;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.LearningSessions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Tests.Sessions;

/// <summary>Local copies of published course versions, as Learning's consumer stores them.</summary>
internal static class CourseVersionCopies
{
    public static CourseVersionCopy Of(Guid courseVersionId, params (Guid ChapterId, int Position, ChapterKind Kind)[] chapters) =>
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
                VideoDurationSeconds = chapter.Kind == ChapterKind.Video ? 420 : null,
            })],
        };
}
