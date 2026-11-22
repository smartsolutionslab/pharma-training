using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.DTOs;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Queries.GetCourseVersion;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;
using static SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions.CourseVersionBuilder;
using static SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions.QuestionDrafts;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions;

public class GetCourseVersionQuestionsTests
{
    [Fact]
    public async Task A_version_is_read_with_its_questions_in_order_counted_from_one_with_their_review()
    {
        var store = new InMemoryCourseVersionStore();
        var version = DraftWithApprovedQuestion(out var first, out var approved);
        var second = ChapterId.New();
        version.AddChapter(TextChapter(second, "Flächendesinfektion", "Wischen.")).IsSuccess.ShouldBeTrue();
        var unreviewed = version.AddQuestion(QuestionId.New(), MultipleChoice(second), AnswerOptionId.New).Value;
        store.Stored.Add(version);

        var read = await new GetCourseVersionHandler(store).HandleAsync(new GetCourseVersion(version.Id.Value), TestContext.Current.CancellationToken);

        var questions = read.Value.Questions;
        questions.Select(question => (question.Id, question.Position, question.ChapterId)).ShouldBe(
        [
            (approved.Value, 1, first.Value),
            (unreviewed.Value, 2, second.Value),
        ]);
        questions[0].Review.ShouldBe(EditorialQuestionReview.Approved);
        questions[0].ReviewedBy.ShouldBe(Author.Value);
        questions[0].ReviewedAt.ShouldBe(Now.Value);
        questions[1].Review.ShouldBe(EditorialQuestionReview.Unreviewed);
        questions[1].ReviewedBy.ShouldBeNull();
        questions.ShouldAllBe(question => question.Provenance == EditorialQuestionProvenance.Human);
    }
}
