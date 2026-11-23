using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.ReadModels;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.LearningSessions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.DTOs;

public static class LearningSessionMappings
{
    /// <summary>The session's view, its chapters in the reading order of its version's copy.</summary>
    public static LearningSessionDto ToDto(this LearningSessionView session, CourseVersionCopy version)
    {
        Ensure.That(session).IsNotNull();
        Ensure.That(version).IsNotNull();

        var chapters = session.Chapters.ToDictionary(chapter => chapter.ChapterId);
        return new LearningSessionDto(
            session.Id,
            session.CourseId,
            session.CourseVersionId,
            ToStatus(session.Status),
            [.. version.Chapters
                .Where(chapter => chapters.ContainsKey(chapter.ChapterId))
                .OrderBy(chapter => chapter.Position)
                .Select(chapter => ToChapterDto(chapters[chapter.ChapterId], chapter.Position))]);
    }

    private static LearningSessionChapterDto ToChapterDto(LearningSessionChapterView chapter, int position) =>
        new(chapter.ChapterId, position, ToKind(chapter.Kind), chapter.ProgressPercent, chapter.Completed);

    private static LearningSessionStatus ToStatus(SessionStatus status) => status switch
    {
        SessionStatus.InProgress => LearningSessionStatus.InProgress,
        SessionStatus.ContentCompleted => LearningSessionStatus.ContentCompleted,
        SessionStatus.Finished => LearningSessionStatus.Finished,
        _ => throw new InvalidOperationException($"Session status {status} has no counterpart on the wire."),
    };

    private static LearningChapterKind ToKind(ChapterKind kind) => kind switch
    {
        ChapterKind.Text => LearningChapterKind.Text,
        ChapterKind.Pdf => LearningChapterKind.Pdf,
        ChapterKind.Video => LearningChapterKind.Video,
        _ => throw new InvalidOperationException($"Chapter kind {kind} has no counterpart on the wire."),
    };
}
