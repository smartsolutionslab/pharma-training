using SmartSolutionsLab.PharmaTraining.Contracts.Editorial;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.Consumers;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.ReadModels;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Tests.Consumers;

public class TestDefinitionPublishedV1ConsumerTests
{
    [Fact]
    public async Task A_published_test_definition_is_stored_with_its_questions_options_and_correctness_in_order()
    {
        var copies = new InMemoryTestDefinitionCopies();
        var courseId = Guid.CreateVersion7();
        var courseVersionId = Guid.CreateVersion7();
        var single = TestDefinitionEvents.SingleChoice("Ist Händewaschen Pflicht?");
        var multiple = TestDefinitionEvents.MultipleChoice("Was gehört zur Schutzkleidung?");

        await new TestDefinitionPublishedV1Consumer(copies, copies).HandleAsync(
            TestDefinitionEvents.Published(courseId, courseVersionId, 80, 3, [single, multiple]),
            TestContext.Current.CancellationToken);

        var copy = copies.Definitions.ShouldHaveSingleItem();
        copy.CourseVersionId.ShouldBe(courseVersionId);
        copy.CourseId.ShouldBe(courseId);
        copy.PassingScorePercent.ShouldBe(80);
        copy.MaxAttempts.ShouldBe(3);
        copy.Questions
            .OrderBy(question => question.Position)
            .Select(question => (question.CourseVersionId, question.QuestionId, question.Position, question.ChapterId, question.Type, question.Text))
            .ShouldBe(
            [
                (courseVersionId, single.QuestionId, 1, single.ChapterId, TestQuestionType.SingleChoice, single.Text),
                (courseVersionId, multiple.QuestionId, 2, multiple.ChapterId, TestQuestionType.MultipleChoice, multiple.Text),
            ]);
        copy.Questions
            .OrderBy(question => question.Position)
            .SelectMany(question => question.Options.OrderBy(option => option.Position))
            .Select(option => (option.CourseVersionId, option.QuestionId, option.OptionId, option.Position, option.Text, option.IsCorrect))
            .ShouldBe(
            [
                (courseVersionId, single.QuestionId, single.Options[0].AnswerOptionId, 1, "Ja", true),
                (courseVersionId, single.QuestionId, single.Options[1].AnswerOptionId, 2, "Nein", false),
                (courseVersionId, multiple.QuestionId, multiple.Options[0].AnswerOptionId, 1, "Handschuhe", true),
                (courseVersionId, multiple.QuestionId, multiple.Options[1].AnswerOptionId, 2, "Kittel", true),
                (courseVersionId, multiple.QuestionId, multiple.Options[2].AnswerOptionId, 3, "Schmuck", false),
            ]);
        copies.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task A_definition_delivered_twice_is_stored_once_as_it_first_arrived()
    {
        var copies = new InMemoryTestDefinitionCopies();
        var consumer = new TestDefinitionPublishedV1Consumer(copies, copies);
        var first = TestDefinitionEvents.Published(Guid.CreateVersion7(), Guid.CreateVersion7(), 80, 3, [TestDefinitionEvents.SingleChoice("Erste Frage")]);

        await consumer.HandleAsync(first, TestContext.Current.CancellationToken);

        // The same version again, republished under a new event identifier and with other content: the first copy wins.
        await consumer.HandleAsync(
            TestDefinitionEvents.Published(first.CourseId, first.CourseVersionId, 50, 5, [TestDefinitionEvents.MultipleChoice("Andere Frage"), TestDefinitionEvents.SingleChoice("Noch eine")]),
            TestContext.Current.CancellationToken);

        var copy = copies.Definitions.ShouldHaveSingleItem();
        copy.PassingScorePercent.ShouldBe(80);
        copy.MaxAttempts.ShouldBe(3);
        copy.Questions.ShouldHaveSingleItem().QuestionId.ShouldBe(first.Questions[0].QuestionId);
        copies.Commits.ShouldBe(1);
    }

    [Theory]
    [InlineData("empty course id", nameof(TestDefinitionPublishedV1.CourseId))]
    [InlineData("empty course version id", nameof(TestDefinitionPublishedV1.CourseVersionId))]
    [InlineData("passing score 101", nameof(TestDefinitionPublishedV1.PassingScorePercent))]
    [InlineData("max attempts 0", nameof(TestDefinitionPublishedV1.MaxAttempts))]
    [InlineData("no questions", nameof(TestDefinitionPublishedV1.Questions))]
    [InlineData("a duplicate question id", nameof(TestQuestionV1.QuestionId))]
    [InlineData("an empty chapter id", nameof(TestQuestionV1.ChapterId))]
    [InlineData("a blank question text", nameof(TestQuestionV1.Text))]
    [InlineData("a question without options", nameof(TestQuestionV1.Options))]
    [InlineData("a duplicate option id", nameof(AnswerOptionV1.AnswerOptionId))]
    [InlineData("single choice with two correct options", nameof(AnswerOptionV1.IsCorrect))]
    [InlineData("multiple choice with no correct option", nameof(AnswerOptionV1.IsCorrect))]
    public async Task A_definition_breaking_a_rule_is_refused_by_the_field_it_breaks(string malformation, string field)
    {
        var copies = new InMemoryTestDefinitionCopies();

        var refused = await Should.ThrowAsync<GuardException>(() =>
            new TestDefinitionPublishedV1Consumer(copies, copies).HandleAsync(Malformed(malformation), TestContext.Current.CancellationToken));

        refused.ParamName.ShouldBe(field);
        copies.Definitions.ShouldBeEmpty();
        copies.Commits.ShouldBe(0);
    }

    private static TestDefinitionPublishedV1 Malformed(string malformation)
    {
        var single = TestDefinitionEvents.SingleChoice("Ist Händewaschen Pflicht?");
        var multiple = TestDefinitionEvents.MultipleChoice("Was gehört zur Schutzkleidung?");
        var wellFormed = TestDefinitionEvents.Published(Guid.CreateVersion7(), Guid.CreateVersion7(), 80, 3, [single, multiple]);
        return malformation switch
        {
            "empty course id" => wellFormed with { CourseId = Guid.Empty },
            "empty course version id" => wellFormed with { CourseVersionId = Guid.Empty },
            "passing score 101" => wellFormed with { PassingScorePercent = 101 },
            "max attempts 0" => wellFormed with { MaxAttempts = 0 },
            "no questions" => wellFormed with { Questions = [] },
            "a duplicate question id" => wellFormed with { Questions = [single, multiple with { QuestionId = single.QuestionId }] },
            "an empty chapter id" => wellFormed with { Questions = [single, multiple with { ChapterId = Guid.Empty }] },
            "a blank question text" => wellFormed with { Questions = [single, multiple with { Text = "  " }] },
            "a question without options" => wellFormed with { Questions = [single, multiple with { Options = [] }] },
            "a duplicate option id" => wellFormed with { Questions = [single, multiple with { Options = [multiple.Options[0], multiple.Options[1] with { AnswerOptionId = multiple.Options[0].AnswerOptionId }] }] },
            "single choice with two correct options" => wellFormed with { Questions = [single with { Options = [single.Options[0], single.Options[1] with { IsCorrect = true }] }, multiple] },
            "multiple choice with no correct option" => wellFormed with { Questions = [single, multiple with { Options = [.. multiple.Options.Select(option => option with { IsCorrect = false })] }] },
            _ => throw new InvalidOperationException($"{malformation} is no malformation this test knows."),
        };
    }
}
