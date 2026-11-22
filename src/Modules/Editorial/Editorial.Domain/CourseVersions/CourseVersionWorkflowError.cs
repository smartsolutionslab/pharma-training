using System.Runtime.CompilerServices;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

/// <summary>
/// What a step of the workflow (<see cref="CourseVersion.SubmitForReview"/>, <see cref="CourseVersion.ReturnToDraft"/>,
/// <see cref="CourseVersion.Publish"/>, <see cref="CourseVersion.Retire"/>) refuses: a closed union of its cases, so a
/// <c>switch</c> on it that misses one does not compile.
/// </summary>
/// <remarks>
/// Each case derives from this record rather than being wrapped by it, so a case is still a <see cref="DomainError"/> the
/// version's other methods return, and a workflow error is the case itself (its <c>Code</c>, its type, its equality)
/// wherever it goes. The compiler reads the cases from <see cref="IUnionMembers"/>; <c>Value</c> is the case itself.
/// </remarks>
[Union]
public abstract record CourseVersionWorkflowError(string Code, string Message) : DomainError(Code, Message), CourseVersionWorkflowError.IUnionMembers
{
    /// <summary>The cases of the union: one <c>Create</c> per case, and the case the error is.</summary>
    public interface IUnionMembers
    {
        object? Value { get; }

        static abstract CourseVersionWorkflowError Create(NotADraft refusal);

        static abstract CourseVersionWorkflowError Create(NotInReview refusal);

        static abstract CourseVersionWorkflowError Create(NotPublished refusal);

        static abstract CourseVersionWorkflowError Create(NoChapters refusal);

        static abstract CourseVersionWorkflowError Create(TooManyQuestions refusal);

        static abstract CourseVersionWorkflowError Create(TestIncomplete refusal);

        static abstract CourseVersionWorkflowError Create(QuestionNotApproved refusal);

        static abstract CourseVersionWorkflowError Create(FourEyesRequired refusal);

        static abstract CourseVersionWorkflowError Create(PublisherApprovedQuestions refusal);

        static abstract CourseVersionWorkflowError Create(TooFewOptions refusal);

        static abstract CourseVersionWorkflowError Create(SingleChoiceNeedsOneCorrect refusal);

        static abstract CourseVersionWorkflowError Create(MultipleChoiceNeedsACorrect refusal);
    }

    object? IUnionMembers.Value => this;

    static CourseVersionWorkflowError IUnionMembers.Create(NotADraft refusal) => refusal;

    static CourseVersionWorkflowError IUnionMembers.Create(NotInReview refusal) => refusal;

    static CourseVersionWorkflowError IUnionMembers.Create(NotPublished refusal) => refusal;

    static CourseVersionWorkflowError IUnionMembers.Create(NoChapters refusal) => refusal;

    static CourseVersionWorkflowError IUnionMembers.Create(TooManyQuestions refusal) => refusal;

    static CourseVersionWorkflowError IUnionMembers.Create(TestIncomplete refusal) => refusal;

    static CourseVersionWorkflowError IUnionMembers.Create(QuestionNotApproved refusal) => refusal;

    static CourseVersionWorkflowError IUnionMembers.Create(FourEyesRequired refusal) => refusal;

    static CourseVersionWorkflowError IUnionMembers.Create(PublisherApprovedQuestions refusal) => refusal;

    static CourseVersionWorkflowError IUnionMembers.Create(TooFewOptions refusal) => refusal;

    static CourseVersionWorkflowError IUnionMembers.Create(SingleChoiceNeedsOneCorrect refusal) => refusal;

    static CourseVersionWorkflowError IUnionMembers.Create(MultipleChoiceNeedsACorrect refusal) => refusal;
}
