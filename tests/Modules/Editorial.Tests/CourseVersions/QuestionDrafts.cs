using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;
using static SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions.CourseVersionBuilder;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions;

/// <summary>Questions as an editor drafts them, complete or not, and drafts that already hold one.</summary>
internal static class QuestionDrafts
{
    public static DraftedQuestion Drafted(ChapterId chapter, string text, QuestionType type, params (string Text, bool Correct)[] options) =>
        new(
            QuestionText.From(text),
            type,
            chapter,
            [.. options.Select(option => (AnswerText.From(option.Text), option.Correct ? Correctness.Correct : Correctness.Incorrect))]);

    public static DraftedQuestion MultipleChoice(ChapterId chapter) =>
        Drafted(chapter, "Welche Flächen werden desinfiziert?", QuestionType.MultipleChoice, ("Rezepturtisch", true), ("Waage", true), ("Schaufenster", false));

    public static DraftedQuestion TwoCorrectSingleChoice(ChapterId chapter) =>
        Drafted(chapter, "Wann desinfizieren Sie die Hände?", QuestionType.SingleChoice, ("Vor der Rezeptur", true), ("Nach der Rezeptur", true));

    public static DraftedQuestion NoCorrectMultipleChoice(ChapterId chapter) =>
        Drafted(chapter, "Welche Flächen werden desinfiziert?", QuestionType.MultipleChoice, ("Schaufenster", false), ("Parkplatz", false));

    public static DraftedQuestion OneOption(ChapterId chapter) =>
        Drafted(chapter, "Wann desinfizieren Sie die Hände?", QuestionType.SingleChoice, ("Vor der Rezeptur", true));

    /// <summary>A draft with one chapter and one unreviewed single-choice question about it.</summary>
    public static CourseVersion DraftWithQuestion(out ChapterId chapter, out QuestionId question)
    {
        var version = DraftWithChapter(out chapter);
        question = version.AddQuestion(QuestionId.New(), SingleChoice(chapter), AnswerOptionId.New).Value;
        return version;
    }

    /// <summary>A draft with one chapter and one question about it, approved by the author.</summary>
    public static CourseVersion DraftWithApprovedQuestion(out ChapterId chapter, out QuestionId question)
    {
        var version = DraftWithQuestion(out chapter, out question);
        version.ApproveQuestion(question, Author, Now).IsSuccess.ShouldBeTrue();
        return version;
    }
}
