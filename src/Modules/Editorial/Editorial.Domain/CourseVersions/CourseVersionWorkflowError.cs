namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

/// <summary>
/// Every way a step of the workflow (<see cref="CourseVersion.SubmitForReview"/>, <see cref="CourseVersion.ReturnToDraft"/>,
/// <see cref="CourseVersion.Publish"/>, <see cref="CourseVersion.Retire"/>) refuses: a closed union of its cases, so only
/// a listed record converts to it and a <c>switch</c> on it that misses one does not compile.
/// </summary>
public union CourseVersionWorkflowError(NotADraft, NotInReview, NotPublished, NoChapters, TooManyQuestions, TestIncomplete, QuestionNotApproved, FourEyesRequired, PublisherApprovedQuestions, TooFewOptions, SingleChoiceNeedsOneCorrect, MultipleChoiceNeedsACorrect);
