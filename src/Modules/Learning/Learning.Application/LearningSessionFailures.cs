namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Application;

/// <summary>Everything the learning session's use cases refuse, each with its own code.</summary>
public static class LearningSessionFailures
{
    public static ApiError CourseVersionNotFound(Guid id) => new CourseVersionNotFound(id);

    public static ApiError SessionNotFound(Guid id) => new LearningSessionNotFound(id);

    public static ApiError IdentityIncomplete() => new IdentityIncomplete();
}
