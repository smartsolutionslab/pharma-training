namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Application;

/// <summary>Everything a learner's request for a chapter's media is refused with, each with its own code (#41).</summary>
public static class LearningMediaFailures
{
    public static ApiError ChapterNotFound(Guid id) => new LearningChapterNotFound(id);

    public static ApiError NoSessionOnVersion(Guid chapterId) => new NoSessionOnVersion(chapterId);

    public static ApiError HasNoMedia(Guid chapterId) => new ChapterHasNoMedia(chapterId);

    public static ApiError NotReady(Guid chapterId) => new ChapterMediaNotReady(chapterId);
}
