namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

/// <summary>
/// Everything the CourseVersion aggregate refuses, each with its own code; the Application maps them to an ApiError.
/// A step of the workflow refuses with the same records as a <see cref="CourseVersionWorkflowError"/>, through
/// <see cref="Workflow"/>.
/// </summary>
public static class CourseVersionErrors
{
    public static DomainError NotADraft(CourseVersionId version, CourseVersionStatus status) => new NotADraft(version, status);

    public static DomainError NotInReview(CourseVersionId version, CourseVersionStatus status) => new NotInReview(version, status);

    public static DomainError NotPublished(CourseVersionId version, CourseVersionStatus status) => new NotPublished(version, status);

    public static DomainError ChapterNotFound(CourseVersionId version, ChapterId chapter) => new ChapterNotFound(version, chapter);

    public static DomainError OrderDoesNotMatchChapters(CourseVersionId version) => new OrderDoesNotMatchChapters(version);

    public static DomainError QuestionNotFound(CourseVersionId version, QuestionId question) => new QuestionNotFound(version, question);

    public static DomainError NoChapters(CourseVersionId version) => new NoChapters(version);

    public static DomainError TestIncomplete(CourseVersionId version, QuestionCount expected, QuestionCount actual) => new TestIncomplete(version, expected, actual);

    public static DomainError TooManyQuestions(CourseVersionId version) => new TooManyQuestions(version);

    public static DomainError TooFewQuestions(QuestionCount requested) => new TooFewQuestions(requested);

    public static DomainError QuestionNotApproved(CourseVersionId version, QuestionId question) => new QuestionNotApproved(version, question);

    public static DomainError QuestionAlreadyApproved(CourseVersionId version, QuestionId question) => new QuestionAlreadyApproved(version, question);

    public static DomainError FourEyesRequired(CourseVersionId version) => new FourEyesRequired(version);

    public static DomainError PublisherApprovedQuestions(CourseVersionId version) => new PublisherApprovedQuestions(version);

    public static DomainError TooFewOptions(QuestionId question) => new TooFewOptions(question);

    public static DomainError SingleChoiceNeedsOneCorrect(QuestionId question) => new SingleChoiceNeedsOneCorrect(question);

    public static DomainError MultipleChoiceNeedsACorrect(QuestionId question) => new MultipleChoiceNeedsACorrect(question);

    /// <summary>
    /// The refusals of the workflow (<see cref="CourseVersion.SubmitForReview"/>, <see cref="CourseVersion.ReturnToDraft"/>,
    /// <see cref="CourseVersion.Publish"/>, <see cref="CourseVersion.Retire"/>): the same records, each returned as the
    /// <see cref="CourseVersionWorkflowError"/> those steps fail with.
    /// </summary>
    public static class Workflow
    {
        public static CourseVersionWorkflowError NotADraft(CourseVersionId version, CourseVersionStatus status) => new NotADraft(version, status);

        public static CourseVersionWorkflowError NotInReview(CourseVersionId version, CourseVersionStatus status) => new NotInReview(version, status);

        public static CourseVersionWorkflowError NotPublished(CourseVersionId version, CourseVersionStatus status) => new NotPublished(version, status);

        public static CourseVersionWorkflowError NoChapters(CourseVersionId version) => new NoChapters(version);

        public static CourseVersionWorkflowError TestIncomplete(CourseVersionId version, QuestionCount expected, QuestionCount actual) => new TestIncomplete(version, expected, actual);

        public static CourseVersionWorkflowError TooManyQuestions(CourseVersionId version) => new TooManyQuestions(version);

        public static CourseVersionWorkflowError QuestionNotApproved(CourseVersionId version, QuestionId question) => new QuestionNotApproved(version, question);

        public static CourseVersionWorkflowError FourEyesRequired(CourseVersionId version) => new FourEyesRequired(version);

        public static CourseVersionWorkflowError PublisherApprovedQuestions(CourseVersionId version) => new PublisherApprovedQuestions(version);

        public static CourseVersionWorkflowError TooFewOptions(QuestionId question) => new TooFewOptions(question);

        public static CourseVersionWorkflowError SingleChoiceNeedsOneCorrect(QuestionId question) => new SingleChoiceNeedsOneCorrect(question);

        public static CourseVersionWorkflowError MultipleChoiceNeedsACorrect(QuestionId question) => new MultipleChoiceNeedsACorrect(question);
    }
}
