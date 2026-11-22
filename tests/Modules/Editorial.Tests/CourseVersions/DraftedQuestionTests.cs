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

    private static (string Text, bool Correct)[] Options(int count) =>
        [.. Enumerable.Range(1, count).Select(number => ($"Fläche {number}", number == 1))];
}
