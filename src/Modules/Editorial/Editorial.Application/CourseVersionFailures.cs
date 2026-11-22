using System.Net;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.TestDrafting;
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

    /// <summary>The AI test draft asks only about text chapters, and the version has none.</summary>
    public static ApiError NoTextChapters() => new NoTextChapters();

    /// <summary>The approved questions already fill the blueprint, so the AI test draft has nothing to add.</summary>
    public static ApiError NothingToDraft() => new NothingToDraft();

    /// <summary>The text chapters are too long to send to the model in one AI test draft request.</summary>
    public static ApiError TooMuchContentToDraft() => new TooMuchContentToDraft();

    /// <summary>The model's answer to an AI test draft could not be used; <paramref name="defect"/> says why.</summary>
    public static ApiError AiTestDraftMalformed(TestDraftDefect defect) => new AiTestDraftMalformed(defect);

    /// <summary>
    /// A refusal of the workflow, with its own code: a question whose content breaks the rules of its shape cannot be
    /// processed until it is edited, a publish under four eyes by the one who submitted the version or approved one of
    /// its questions is forbidden to this caller, and every other refusal is a conflict with the version's state. The
    /// union is closed and the switch has no discard arm, so a new case fails the build until it is mapped here.
    /// </summary>
    public static ApiError From(CourseVersionWorkflowError error) => error switch
    {
        NotADraft or NotInReview or NotPublished or NoChapters or TooManyQuestions or TestIncomplete or QuestionNotApproved
            => ApiError.From(error, HttpStatusCode.Conflict),
        FourEyesRequired or PublisherApprovedQuestions => ApiError.From(error, HttpStatusCode.Forbidden),
        TooFewOptions or SingleChoiceNeedsOneCorrect or MultipleChoiceNeedsACorrect => ApiError.From(error, HttpStatusCode.UnprocessableEntity),
    };

    /// <summary>
    /// A rule of the version, with its own code: a refusal of the workflow as <see cref="From(CourseVersionWorkflowError)"/>
    /// maps it, a chapter or question it does not hold is not found, a blueprint with too few questions is a bad request,
    /// anything else is a conflict with its state. <see cref="DomainError"/> is no closed union, so the last arm takes
    /// the rules no arm names.
    /// </summary>
    public static ApiError From(DomainError error) => error switch
    {
        CourseVersionWorkflowError workflow => From(workflow),
        ChapterNotFound or QuestionNotFound => ApiError.From(error, HttpStatusCode.NotFound),
        TooFewQuestions => ApiError.From(error, HttpStatusCode.BadRequest),
        // This arm documents the rule that these refusals are conflicts with the version's state; it does not enforce it,
        // since the discard arm below answers 409 for them all the same.
        OrderDoesNotMatchChapters or QuestionAlreadyApproved => ApiError.From(error, HttpStatusCode.Conflict),
        _ => ApiError.From(error, HttpStatusCode.Conflict),
    };
}
