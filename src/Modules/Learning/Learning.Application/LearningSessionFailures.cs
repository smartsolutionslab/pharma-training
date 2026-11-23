using System.Net;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.LearningSessions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Application;

/// <summary>Everything the learning session's use cases refuse, each with its own code.</summary>
public static class LearningSessionFailures
{
    public static ApiError CourseVersionNotFound(Guid id) => new CourseVersionNotFound(id);

    public static ApiError CourseVersionRetired(Guid id) => new CourseVersionRetired(id);

    public static ApiError SessionNotFound(Guid id) => new LearningSessionNotFound(id);

    public static ApiError IdentityIncomplete() => new IdentityIncomplete();

    /// <summary>A report that lost the append race on its session's stream.</summary>
    public static ApiError Changed(Guid id) => new LearningSessionChanged(id);

    /// <summary>
    /// A refused progress report: a finished session conflicts with it; a chapter it does not hold, or video progress
    /// faster than playback (ADR 0021), is a semantic error of the request.
    /// </summary>
    public static ApiError From(RecordProgressError error) => error switch
    {
        SessionFinishedAlready finished => ApiError.From(finished, HttpStatusCode.Conflict),
        ChapterNotInCourse outside => ApiError.From(outside, HttpStatusCode.UnprocessableEntity),
        ProgressTooFast tooFast => ApiError.From(tooFast, HttpStatusCode.UnprocessableEntity),
    };
}
