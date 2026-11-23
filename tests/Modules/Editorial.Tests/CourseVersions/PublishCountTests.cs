using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions.Events;
using static SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions.CourseVersionBuilder;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions;

/// <summary>
/// Publish checks the question count against the blueprint itself (review 6013596091 on #151), rather than trusting
/// that SubmitForReview did: a version loaded in review with more questions than its blueprint asks for, every one of
/// them approved and sound, is refused as incomplete under any policy.
/// </summary>
public class PublishCountTests
{
    [Theory]
    [InlineData(ReviewPolicy.SingleEditor)]
    [InlineData(ReviewPolicy.FourEyes)]
    public void Publishing_a_version_in_review_whose_questions_no_longer_match_its_blueprint_reports_the_test_incomplete(ReviewPolicy policy)
    {
        var version = InReview();
        var stray = ReadyToSubmit();
        stray.Questions.ShouldHaveSingleItem().Review.ShouldBe(ReviewState.Approved);

        // A row the rules would refuse, loaded by EF Core past them: the version in review now holds two approved
        // questions while its blueprint asks for one.
        version.LoadQuestions([stray.Questions[0]]);
        version.Questions.Count.ShouldBe(2);
        version.Blueprint.Questions.ShouldBe(QuestionCount.From(1));

        var published = Should.NotThrow(() => version.Publish(Reviewer, policy, Now));

        published.IsFailure.ShouldBeTrue("a version whose questions do not match its blueprint was published");
        CourseVersionFailures.From(published.Error).Code.ShouldBe("editorial.test-incomplete");
        version.Status.ShouldBe(CourseVersionStatus.InReview);
        version.PublishedBy.ShouldBeNull();
        version.PublishedAt.ShouldBeNull();
        version.DomainEvents.OfType<CourseVersionPublished>().ShouldBeEmpty();
    }
}
