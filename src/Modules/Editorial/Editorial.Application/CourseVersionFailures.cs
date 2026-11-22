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

    public static ApiError InvalidQuestionText() => new InvalidQuestionText();

    public static ApiError InvalidQuestionType() => new InvalidQuestionType();

    public static ApiError InvalidAnswerOptions() => new InvalidAnswerOptions();

    public static ApiError InvalidAnswerText() => new InvalidAnswerText();

    public static ApiError IdentityIncomplete() => new IdentityIncomplete();

    public static ApiError InvalidUploadKind() => new InvalidUploadKind();

    public static ApiError InvalidChapterKind() => new InvalidChapterKind();

    public static ApiError ChapterContentDoesNotMatchKind() => new ChapterContentDoesNotMatchKind();

    public static ApiError InvalidChapterFile() => new InvalidChapterFile();

    public static ApiError InvalidVideoDuration() => new InvalidVideoDuration();

    public static ApiError UploadNotFound() => new UploadNotFound();

    public static ApiError UploadNotAccepted() => new UploadNotAccepted();

    public static ApiError NotFound(Guid id) => new CourseVersionNotFound(id);

    /// <summary>A save that lost a race: another change to the same version was saved since this one loaded it.</summary>
    public static ApiError Changed(Guid id) => new CourseVersionChanged(id);

    public static ApiError UnknownChapter(Guid version, Guid chapter) => new UnknownChapter(version, chapter);

    public static ApiError UnknownQuestion(Guid version, Guid question) => new UnknownQuestion(version, question);

    public static ApiError InvalidQuestionCount() => new InvalidQuestionCount();

    public static ApiError InvalidPassingScore() => new InvalidPassingScore();

    public static ApiError InvalidValidity() => new InvalidValidity();

    /// <summary>
    /// A rule of the version, with its own code: a chapter or question it does not hold is not found, a question whose
    /// content breaks the rules of its shape cannot be processed until it is edited, anything else is a conflict with
    /// its state. <see cref="DomainError"/> is no closed union, so the last arm takes the rules no arm names.
    /// </summary>
    public static ApiError From(DomainError error) => error switch
    {
        ChapterNotFound or QuestionNotFound => ApiError.From(error, HttpStatusCode.NotFound),
        TooFewOptions or SingleChoiceNeedsOneCorrect or MultipleChoiceNeedsACorrect => ApiError.From(error, HttpStatusCode.UnprocessableEntity),
        NotADraft or OrderDoesNotMatchChapters or NotInReview or NoChapters or TestIncomplete
            => ApiError.From(error, HttpStatusCode.Conflict),
        _ => ApiError.From(error, HttpStatusCode.Conflict),
    };
}
