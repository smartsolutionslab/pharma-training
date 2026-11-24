using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using System.Net;
using System.Runtime.CompilerServices;
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

    /// <summary>An approval (#205) or a return (#220) whose token names no display name that fits a <see cref="ReviewerName"/>.</summary>
    public static ApiError ReviewerNameMissing() => new ReviewerNameMissing();

    /// <summary>A return's reason that is missing, blank, too long or holds a control or format character.</summary>
    public static ApiError InvalidReturnReason() => new InvalidReturnReason();

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

    /// <summary>Another save came at the same moment and PostgreSQL ended this one; try again.</summary>
    public static ApiError ConcurrentSave() => new EditorialConcurrentSave();

    /// <summary>How a save of the course version lost a race.</summary>
    public static ApiError LostRace(LostRace race, Guid id) => race switch
    {
        VersionMovedOn => Changed(id),
        SaveDeadlocked => ConcurrentSave(),
    };

    /// <summary>The version's successor already exists.</summary>
    public static ApiError NextVersionExists() => new NextVersionExists();

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
        NotADraft refusal => ApiError.From(refusal, HttpStatusCode.Conflict),
        NotInReview refusal => ApiError.From(refusal, HttpStatusCode.Conflict),
        NotPublished refusal => ApiError.From(refusal, HttpStatusCode.Conflict),
        NoChapters refusal => ApiError.From(refusal, HttpStatusCode.Conflict),
        TooManyQuestions refusal => ApiError.From(refusal, HttpStatusCode.Conflict),
        TestIncomplete refusal => ApiError.From(refusal, HttpStatusCode.Conflict),
        QuestionNotApproved refusal => ApiError.From(refusal, HttpStatusCode.Conflict),
        FourEyesRequired refusal => ApiError.From(refusal, HttpStatusCode.Forbidden),
        PublisherApprovedQuestions refusal => ApiError.From(refusal, HttpStatusCode.Forbidden),
        TooFewOptions refusal => ApiError.From(refusal, HttpStatusCode.UnprocessableEntity),
        SingleChoiceNeedsOneCorrect refusal => ApiError.From(refusal, HttpStatusCode.UnprocessableEntity),
        MultipleChoiceNeedsACorrect refusal => ApiError.From(refusal, HttpStatusCode.UnprocessableEntity),
    };

    /// <summary>
    /// A rule of the version, with its own code: a chapter or question it does not hold is not found, a question whose
    /// content breaks the rules of its shape cannot be processed until it is edited, a blueprint with too few questions
    /// is a bad request, a publish under four eyes by the one who submitted the version or approved one of its questions
    /// is forbidden to this caller, anything else is a conflict with its state. <see cref="DomainError"/> is no closed
    /// union, so the last arm takes the rules no arm names.
    /// </summary>
    /// <remarks>
    /// A case of <see cref="CourseVersionWorkflowError"/> converts to that union as well as to its base, so a call with the
    /// record itself would match both overloads; this one takes it, and answers it with the same status.
    /// </remarks>
    [OverloadResolutionPriority(1)]
    public static ApiError From(DomainError error) => error switch
    {
        ChapterNotFound or QuestionNotFound => ApiError.From(error, HttpStatusCode.NotFound),
        TooFewOptions or SingleChoiceNeedsOneCorrect or MultipleChoiceNeedsACorrect => ApiError.From(error, HttpStatusCode.UnprocessableEntity),
        TooFewQuestions => ApiError.From(error, HttpStatusCode.BadRequest),
        FourEyesRequired or PublisherApprovedQuestions => ApiError.From(error, HttpStatusCode.Forbidden),
        // This arm documents the rule that these refusals are conflicts with the version's state; it does not enforce it,
        // since the discard arm below answers 409 for them all the same.
        NotADraft or OrderDoesNotMatchChapters or FileInUse or TooManyQuestions or NotInReview or NotPublished or NoChapters or TestIncomplete
            or QuestionNotApproved or QuestionAlreadyApproved => ApiError.From(error, HttpStatusCode.Conflict),
        _ => ApiError.From(error, HttpStatusCode.Conflict),
    };
}
