namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

/// <summary>
/// Everything the CourseVersion aggregate refuses, each with its own code; the Application maps them to an ApiError.
/// A refusal of the workflow is a <see cref="CourseVersionWorkflowError"/>, and still a <see cref="DomainError"/>.
/// </summary>
public static class CourseVersionErrors
{
    public static CourseVersionWorkflowError NotADraft(CourseVersionId version, CourseVersionStatus status) => new NotADraft(version, status);

    public static CourseVersionWorkflowError NotInReview(CourseVersionId version, CourseVersionStatus status) => new NotInReview(version, status);

    public static CourseVersionWorkflowError NotPublished(CourseVersionId version, CourseVersionStatus status) => new NotPublished(version, status);

    public static DomainError ChapterNotFound(CourseVersionId version, ChapterId chapter) => new ChapterNotFound(version, chapter);

    public static DomainError OrderDoesNotMatchChapters(CourseVersionId version) => new OrderDoesNotMatchChapters(version);

    public static DomainError QuestionNotFound(CourseVersionId version, QuestionId question) => new QuestionNotFound(version, question);

    public static CourseVersionWorkflowError NoChapters(CourseVersionId version) => new NoChapters(version);

    public static CourseVersionWorkflowError TestIncomplete(CourseVersionId version, QuestionCount expected, QuestionCount actual) => new TestIncomplete(version, expected, actual);

    public static CourseVersionWorkflowError TooManyQuestions(CourseVersionId version) => new TooManyQuestions(version);

    public static DomainError TooFewQuestions(QuestionCount requested) => new TooFewQuestions(requested);

    public static CourseVersionWorkflowError QuestionNotApproved(CourseVersionId version, QuestionId question) => new QuestionNotApproved(version, question);

    public static DomainError QuestionAlreadyApproved(CourseVersionId version, QuestionId question) => new QuestionAlreadyApproved(version, question);

    public static CourseVersionWorkflowError FourEyesRequired(CourseVersionId version) => new FourEyesRequired(version);

    public static CourseVersionWorkflowError PublisherApprovedQuestions(CourseVersionId version) => new PublisherApprovedQuestions(version);

    public static CourseVersionWorkflowError TooFewOptions(QuestionId question) => new TooFewOptions(question);

    public static CourseVersionWorkflowError SingleChoiceNeedsOneCorrect(QuestionId question) => new SingleChoiceNeedsOneCorrect(question);

    public static CourseVersionWorkflowError MultipleChoiceNeedsACorrect(QuestionId question) => new MultipleChoiceNeedsACorrect(question);
}
