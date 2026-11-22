using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;
using static SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions.QuestionDrafts;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions;

/// <summary>A drafted question holds at most ten answer options.</summary>
public class DraftedQuestionTests
{
    [Fact]
    public void A_draft_with_ten_answer_options_is_drafted()
    {
        var draft = Drafted(ChapterId.New(), "Welche Flächen?", QuestionType.MultipleChoice, Options(10));

        draft.Options.Count.ShouldBe(10);
    }

    [Fact]
    public void A_draft_with_eleven_answer_options_is_refused()
    {
        Should.Throw<GuardException>(() => Drafted(ChapterId.New(), "Welche Flächen?", QuestionType.MultipleChoice, Options(11)));
    }

    [Fact]
    public void A_draft_keeps_the_ten_options_it_was_drafted_with_when_its_producer_adds_to_the_list_afterwards()
    {
        List<(AnswerText Text, Correctness Correctness)> options = [.. Enumerable.Range(1, 10).Select(number => (AnswerText.From($"Fläche {number}"), number == 1 ? Correctness.Correct : Correctness.Incorrect))];
        var draft = new DraftedQuestion(QuestionText.From("Welche Flächen?"), QuestionType.MultipleChoice, ChapterId.New(), options);

        options.Add((AnswerText.From("Fläche 11"), Correctness.Incorrect));

        draft.Options.Count.ShouldBe(10, "the draft holds the producer's own list, so an eleventh option added to it afterwards passes the guard");
    }

    private static (string Text, bool Correct)[] Options(int count) =>
        [.. Enumerable.Range(1, count).Select(number => ($"Fläche {number}", number == 1))];
}
