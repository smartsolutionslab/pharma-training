using System.Net;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;

/// <summary>Everything the course versions' use cases refuse, each with its own code.</summary>
public static class CourseVersionFailures
{
    public static ApiError InvalidCourseTitle() => new InvalidCourseTitle();

    public static ApiError InvalidChapterTitle() => new InvalidChapterTitle();

    public static ApiError InvalidChapterText() => new InvalidChapterText();

    public static ApiError InvalidChapterOrder() => new InvalidChapterOrder();

    public static ApiError InvalidCourseVersionStatus() => new InvalidCourseVersionStatus();

    public static ApiError IdentityIncomplete() => new IdentityIncomplete();

    public static ApiError NotFound(Guid id) => new CourseVersionNotFound(id);

    /// <summary>
    /// A rule of the version, with its own code: a chapter it does not hold is not found, anything else is a conflict
    /// with its state. <see cref="DomainError"/> is no closed union, so the last arm takes the rules no arm names.
    /// </summary>
    public static ApiError From(DomainError error) => error switch
    {
        ChapterNotFound => ApiError.From(error, HttpStatusCode.NotFound),
        NotADraft or OrderDoesNotMatchChapters => ApiError.From(error, HttpStatusCode.Conflict),
        _ => ApiError.From(error, HttpStatusCode.Conflict),
    };
}
