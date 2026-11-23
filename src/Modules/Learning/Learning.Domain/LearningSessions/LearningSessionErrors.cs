namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.LearningSessions;

/// <summary>Everything the LearningSession aggregate refuses, each with its own code; the Application maps them to an ApiError.</summary>
public static class LearningSessionErrors
{
    public static RecordProgressError FinishedAlready(LearningSessionId session) => new SessionFinishedAlready(session);

    public static RecordProgressError ChapterNotInCourse(LearningSessionId session, ChapterId chapter) => new ChapterNotInCourse(session, chapter);

    public static RecordProgressError ProgressTooFast(LearningSessionId session, ChapterId chapter) => new ProgressTooFast(session, chapter);
}
