namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

/// <summary>Everything the CourseVersion aggregate refuses, each with its own code; the Application maps them to an ApiError.</summary>
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

    public static DomainError FourEyesRequired(CourseVersionId version) => new FourEyesRequired(version);

    public static DomainError TooFewOptions(QuestionId question) => new TooFewOptions(question);

    public static DomainError SingleChoiceNeedsOneCorrect(QuestionId question) => new SingleChoiceNeedsOneCorrect(question);

    public static DomainError MultipleChoiceNeedsACorrect(QuestionId question) => new MultipleChoiceNeedsACorrect(question);
}
