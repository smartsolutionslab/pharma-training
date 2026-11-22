using System.Net;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;
using static SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions.CourseVersionBuilder;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions;

/// <summary>
/// The errors of the workflow (SubmitForReview, ReturnToDraft, Publish, Retire) are a closed union,
/// <see cref="CourseVersionWorkflowError"/> (AGENTS.md, "Result style"), mapped by
/// <c>CourseVersionFailures.From(CourseVersionWorkflowError)</c> with a switch that has no discard arm: a new case fails
/// the build until it is mapped. These tests pin each case's status and code.
/// </summary>
public class CourseVersionWorkflowErrorTests
{
    private static readonly CourseVersionId Version = CourseVersionId.New();
    private static readonly QuestionId Question = QuestionId.New();

    [Theory]
    [InlineData(nameof(NotADraft), HttpStatusCode.Conflict, "editorial.not-a-draft")]
    [InlineData(nameof(NotInReview), HttpStatusCode.Conflict, "editorial.not-in-review")]
    [InlineData(nameof(NotPublished), HttpStatusCode.Conflict, "editorial.not-published")]
    [InlineData(nameof(NoChapters), HttpStatusCode.Conflict, "editorial.no-chapters")]
    [InlineData(nameof(TooManyQuestions), HttpStatusCode.Conflict, "editorial.too-many-questions")]
    [InlineData(nameof(TestIncomplete), HttpStatusCode.Conflict, "editorial.test-incomplete")]
    [InlineData(nameof(QuestionNotApproved), HttpStatusCode.Conflict, "editorial.question-not-approved")]
    [InlineData(nameof(FourEyesRequired), HttpStatusCode.Forbidden, "editorial.four-eyes")]
    [InlineData(nameof(PublisherApprovedQuestions), HttpStatusCode.Forbidden, "editorial.publisher-approved-questions")]
    [InlineData(nameof(TooFewOptions), HttpStatusCode.UnprocessableEntity, "editorial.too-few-options")]
    [InlineData(nameof(SingleChoiceNeedsOneCorrect), HttpStatusCode.UnprocessableEntity, "editorial.single-choice-one-correct")]
    [InlineData(nameof(MultipleChoiceNeedsACorrect), HttpStatusCode.UnprocessableEntity, "editorial.multiple-choice-a-correct")]
    public void Each_workflow_error_is_mapped_to_its_status_and_code(string name, HttpStatusCode status, string code)
    {
        var error = Case(name);

        var mapped = CourseVersionFailures.From(error);

        mapped.Status.ShouldBe(status);
        mapped.Code.ShouldBe(code);
    }

    /// <summary>Each step of the workflow returns the union, so its caller maps it with the switch that names every case.</summary>
    [Fact]
    public void The_workflow_steps_fail_with_the_workflow_union()
    {
        var submitted = Draft().SubmitForReview(Author);
        var returned = Draft().ReturnToDraft(Reviewer, Now);
        var published = Draft().Publish(Reviewer, ReviewPolicy.SingleEditor, Now);
        var retired = Draft().Retire(Reviewer, Now);

        CourseVersionFailures.From(WorkflowError(submitted)).Code.ShouldBe("editorial.no-chapters");
        CourseVersionFailures.From(WorkflowError(returned)).Code.ShouldBe("editorial.not-in-review");
        CourseVersionFailures.From(WorkflowError(published)).Code.ShouldBe("editorial.not-in-review");
        CourseVersionFailures.From(WorkflowError(retired)).Code.ShouldBe("editorial.not-published");
    }

    /// <summary>Compiles only for a step that returns the workflow union: <see cref="Result{TValue, TError}"/> is invariant.</summary>
    private static CourseVersionWorkflowError WorkflowError(Result<Unit, CourseVersionWorkflowError> result) => result.Error;

    private static CourseVersionWorkflowError Case(string name) => name switch
    {
        nameof(NotADraft) => new NotADraft(Version, CourseVersionStatus.InReview),
        nameof(NotInReview) => new NotInReview(Version, CourseVersionStatus.Draft),
        nameof(NotPublished) => new NotPublished(Version, CourseVersionStatus.Draft),
        nameof(NoChapters) => new NoChapters(Version),
        nameof(TooManyQuestions) => new TooManyQuestions(Version),
        nameof(TestIncomplete) => new TestIncomplete(Version, QuestionCount.From(10), QuestionCount.From(9)),
        nameof(QuestionNotApproved) => new QuestionNotApproved(Version, Question),
        nameof(FourEyesRequired) => new FourEyesRequired(Version),
        nameof(PublisherApprovedQuestions) => new PublisherApprovedQuestions(Version),
        nameof(TooFewOptions) => new TooFewOptions(Question),
        nameof(SingleChoiceNeedsOneCorrect) => new SingleChoiceNeedsOneCorrect(Question),
        nameof(MultipleChoiceNeedsACorrect) => new MultipleChoiceNeedsACorrect(Question),
        _ => throw new InvalidOperationException($"No workflow error is named {name}."),
    };
}
