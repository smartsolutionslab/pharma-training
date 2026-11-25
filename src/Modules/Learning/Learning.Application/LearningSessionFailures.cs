using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
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

    public static ApiError MembershipMissing() => new MembershipMissing();

    /// <summary>A report that lost the append race on its session's stream.</summary>
    public static ApiError Changed(Guid id) => new LearningSessionChanged(id);

    /// <summary>Another save came at the same moment and PostgreSQL ended this one; try again.</summary>
    public static ApiError ConcurrentSave() => new LearningConcurrentSave();

    /// <summary>How a save of the stream lost a race: a stream that moved on is the changed code, a deadlock a concurrent save.</summary>
    public static ApiError LostRace(LostRace race, Guid id) => race switch
    {
        VersionMovedOn => Changed(id),
        SaveDeadlocked => ConcurrentSave(),
    };

    /// <summary>
    /// A refused progress report: a finished session conflicts with it; a chapter it does not hold, or video progress
    /// faster than playback (ADR 0024), is a semantic error of the request.
    /// </summary>
    public static ApiError From(RecordProgressError error) => error switch
    {
        SessionFinishedAlready finished => ApiError.From(finished, HttpStatusCode.Conflict),
        ChapterNotInCourse outside => ApiError.From(outside, HttpStatusCode.UnprocessableEntity),
        ProgressTooFast tooFast => ApiError.From(tooFast, HttpStatusCode.UnprocessableEntity),
    };
}
