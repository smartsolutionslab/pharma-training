using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using System.Net;
using Microsoft.Extensions.Time.Testing;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.AddQuestion;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.ApproveQuestion;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.EditQuestion;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.RemoveQuestion;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;
using static SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions.CourseVersionBuilder;
using static SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions.QuestionDrafts;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions;

/// <summary>A question change whose version another save changed since it was loaded is refused as a conflict, not a fault.</summary>
public class QuestionLostRaceTests
{
    private readonly InMemoryCourseVersionStore store = new();
    private readonly IEditorialUnitOfWork lostRace = Substitute.For<IEditorialUnitOfWork>();
    private readonly CourseVersion version;
    private readonly ChapterId chapter;
    private readonly QuestionId question;

    public QuestionLostRaceTests()
    {
        version = DraftWithQuestion(out chapter, out question);
        store.Stored.Add(version);
        lostRace.TryCommitAsync(Arg.Any<CancellationToken>()).Returns(Failure(LostRaces.VersionMovedOn()));
    }

    [Fact]
    public async Task An_added_question_that_lost_the_race_is_told_the_version_changed()
    {
        var added = await new AddQuestionHandler(store, lostRace)
            .HandleAsync(new AddQuestion(version.Id, MultipleChoice(chapter)), TestContext.Current.CancellationToken);

        ShouldBeChanged(added.Error);
    }

    [Fact]
    public async Task An_edited_question_that_lost_the_race_is_told_the_version_changed()
    {
        var edited = await new EditQuestionHandler(store, lostRace)
            .HandleAsync(new EditQuestion(version.Id, question, MultipleChoice(chapter), Author), TestContext.Current.CancellationToken);

        ShouldBeChanged(edited.Error);
    }

    [Fact]
    public async Task A_removed_question_that_lost_the_race_is_told_the_version_changed()
    {
        var removed = await new RemoveQuestionHandler(store, lostRace)
            .HandleAsync(new RemoveQuestion(version.Id, question), TestContext.Current.CancellationToken);

        ShouldBeChanged(removed.Error);
    }

    [Fact]
    public async Task An_approval_that_lost_the_race_is_told_the_version_changed()
    {
        var approved = await new ApproveQuestionHandler(store, lostRace, new FakeTimeProvider(Now.Value))
            .HandleAsync(new ApproveQuestion(version.Id, question, Reviewer, ReviewersName), TestContext.Current.CancellationToken);

        ShouldBeChanged(approved.Error);
    }

    private void ShouldBeChanged(ApiError error)
    {
        error.ShouldBe(new CourseVersionChanged(version.Id.Value));
        error.Code.ShouldBe("editorial.course-version-changed");
        error.Status.ShouldBe(HttpStatusCode.Conflict);
    }
}
