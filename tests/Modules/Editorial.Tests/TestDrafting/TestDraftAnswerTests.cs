using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.TestDrafting;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;
using static SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.TestDrafting.TestDrafterAskTests;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.TestDrafting;

/// <summary>
/// The model's answer, checked as untrusted input and mapped onto drafted questions: what would break the version or
/// the request is malformed, what an editor fixes before approving is accepted.
/// </summary>
public class TestDraftAnswerTests
{
    [Fact]
    public void A_sound_answer_maps_each_question_to_its_chapters_id_type_text_and_options()
    {
        var version = TestDraftVersions.WithTextChapters(3, "Händehygiene");
        version.AddChapter(Chapter.OfPdf(ChapterId.New(), ChapterTitle.From("Merkblatt"), BlobReference.From("chapters/merkblatt.pdf"), PdfTextExtraction.NoText, null)).IsSuccess.ShouldBeTrue();
        var flaechen = ChapterId.New();
        version.AddChapter(Chapter.OfText(flaechen, ChapterTitle.From("Flächen"), MarkdownText.From("Rezepturtisch und Waage täglich."))).IsSuccess.ShouldBeTrue();
        var haende = TestDraftVersions.TextChapterIds(version)[0];
        var request = TestDraftRequest.For(version).Value;
        var answer = Answer(
            Question(1, TestDraftAnswerType.SingleChoice, "Wann desinfizieren Sie die Hände?", ("Vor der Rezeptur", true), ("Einmal am Tag", false)),
            Question(2, TestDraftAnswerType.MultipleChoice, "Welche Flächen werden desinfiziert?", ("Rezepturtisch", true), ("Waage", true), ("Schaufenster", false)),
            Question(1, TestDraftAnswerType.SingleChoice, "Womit desinfizieren Sie die Hände?", ("Mit Händedesinfektionsmittel", true), ("Mit Wasser", false)));

        var drafted = answer.ToDraftedQuestions(request).Value;

        drafted.Count.ShouldBe(3);
        drafted[0].Chapter.ShouldBe(haende);
        drafted[0].Type.ShouldBe(QuestionType.SingleChoice);
        drafted[0].Text.ShouldBe(QuestionText.From("Wann desinfizieren Sie die Hände?"));
        drafted[0].Options.ShouldBe([(AnswerText.From("Vor der Rezeptur"), Correctness.Correct), (AnswerText.From("Einmal am Tag"), Correctness.Incorrect)]);
        drafted[1].Chapter.ShouldBe(flaechen, "chapter 2 is the second text chapter, not the PDF between them");
        drafted[1].Type.ShouldBe(QuestionType.MultipleChoice);
        drafted[1].Options.Select(option => option.Correctness).ShouldBe([Correctness.Correct, Correctness.Correct, Correctness.Incorrect]);
        drafted[2].Chapter.ShouldBe(haende);
        drafted[2].Text.ShouldBe(QuestionText.From("Womit desinfizieren Sie die Hände?"));
    }

    [Fact]
    public void Texts_are_trimmed_as_the_value_objects_trim_them()
    {
        var request = Request(1, "Händehygiene");
        var answer = Answer(Question(1, TestDraftAnswerType.SingleChoice, "  Wann desinfizieren Sie die Hände?\n", ("  Vor der Rezeptur ", true), ("\tEinmal am Tag", false)));

        var drafted = answer.ToDraftedQuestions(request).Value.ShouldHaveSingleItem();

        drafted.Text.Value.ShouldBe("Wann desinfizieren Sie die Hände?");
        drafted.Options.Select(option => option.Text.Value).ShouldBe(["Vor der Rezeptur", "Einmal am Tag"]);
    }

    [Fact]
    public void Texts_at_their_maximum_length_are_accepted()
    {
        var request = Request(1, "Händehygiene");
        var answer = Answer(Question(1, TestDraftAnswerType.SingleChoice, new string('f', QuestionText.MaxLength), (new string('a', AnswerText.MaxLength), true), ("Nein", false)));

        answer.ToDraftedQuestions(request).IsSuccess.ShouldBeTrue();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public void A_count_other_than_requested_is_malformed_as_wrong_count(int difference)
    {
        var request = Request(4, "Händehygiene", "Flächen");
        var answer = Answer([.. Enumerable.Range(0, 4 + difference).Select(index => Sound(1 + (index % 2)))]);

        Malformed(answer.ToDraftedQuestions(request), TestDraftDefect.WrongCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(-1)]
    public void A_chapter_number_outside_the_chapters_sent_is_malformed_as_unknown_chapter(int chapter)
    {
        var request = Request(2, "Händehygiene", "Flächen");
        var answer = Answer(Sound(1), Sound(chapter));

        Malformed(answer.ToDraftedQuestions(request), TestDraftDefect.UnknownChapter);
    }

    [Theory]
    [InlineData("blank")]
    [InlineData("empty")]
    [InlineData("overlong")]
    [InlineData("nul")]
    public void A_blank_overlong_or_nul_question_text_is_malformed(string kind)
    {
        var request = Request(1, "Händehygiene");
        var answer = Answer(Question(1, TestDraftAnswerType.SingleChoice, Bad(kind, QuestionText.MaxLength), ("Vor der Rezeptur", true), ("Einmal am Tag", false)));

        Malformed(answer.ToDraftedQuestions(request), TestDraftDefect.InvalidQuestionText);
    }

    [Theory]
    [InlineData("blank")]
    [InlineData("empty")]
    [InlineData("overlong")]
    [InlineData("nul")]
    public void A_blank_or_overlong_option_text_is_malformed(string kind)
    {
        var request = Request(1, "Händehygiene");
        var answer = Answer(Question(1, TestDraftAnswerType.SingleChoice, "Wann desinfizieren Sie die Hände?", ("Vor der Rezeptur", true), (Bad(kind, AnswerText.MaxLength), false)));

        Malformed(answer.ToDraftedQuestions(request), TestDraftDefect.InvalidOptionText);
    }

    [Fact]
    public void Eleven_options_are_malformed_as_too_many_options_and_ten_are_not()
    {
        var request = Request(1, "Händehygiene");

        Malformed(Answer(WithOptions(DraftedQuestion.MaxOptions + 1)).ToDraftedQuestions(request), TestDraftDefect.TooManyOptions);
        Answer(WithOptions(DraftedQuestion.MaxOptions)).ToDraftedQuestions(request).Value.ShouldHaveSingleItem().Options.Count.ShouldBe(DraftedQuestion.MaxOptions);
    }

    [Theory]
    [InlineData("no options")]
    [InlineData("one option")]
    [InlineData("single choice with two correct")]
    [InlineData("single choice with none correct")]
    [InlineData("multiple choice with none correct")]
    public void Too_few_options_or_the_wrong_correct_ones_are_accepted_for_the_editor_to_fix(string shape)
    {
        var request = Request(1, "Händehygiene");
        var question = shape switch
        {
            "no options" => Question(1, TestDraftAnswerType.SingleChoice, "Frage?"),
            "one option" => Question(1, TestDraftAnswerType.SingleChoice, "Frage?", ("Ja", true)),
            "single choice with two correct" => Question(1, TestDraftAnswerType.SingleChoice, "Frage?", ("Ja", true), ("Auch ja", true)),
            "single choice with none correct" => Question(1, TestDraftAnswerType.SingleChoice, "Frage?", ("Nein", false), ("Auch nein", false)),
            "multiple choice with none correct" => Question(1, TestDraftAnswerType.MultipleChoice, "Frage?", ("Nein", false), ("Auch nein", false)),
            _ => throw new System.Diagnostics.UnreachableException(shape),
        };

        var result = Answer(question).ToDraftedQuestions(request);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldHaveSingleItem().Options.Count.ShouldBe(question.Options.Count);
    }

    [Fact]
    public void An_uneven_split_with_the_right_total_is_accepted()
    {
        var request = Request(4, "Händehygiene", "Flächen");
        request.Chapters.Select(chapter => chapter.Quota.Value).ShouldBe([2, 2]);
        var answer = Answer(Sound(1), Sound(1), Sound(1), Sound(1));

        var result = answer.ToDraftedQuestions(request);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldAllBe(question => question.Chapter == request.Chapters[0].Id);
    }

    internal static TestDraftAnswer Answer(params TestDraftAnswerQuestion[] questions) => new(questions);

    internal static TestDraftAnswerQuestion Question(int chapter, TestDraftAnswerType type, string text, params (string Text, bool Correct)[] options) =>
        new(chapter, type, text, [.. options.Select(option => new TestDraftAnswerOption(option.Text, option.Correct))]);

    internal static TestDraftAnswerQuestion Sound(int chapter) =>
        Question(chapter, TestDraftAnswerType.SingleChoice, $"Was gilt laut Kapitel {chapter}?", ("Das Richtige", true), ("Das Falsche", false), ("Das Andere", false), ("Das Übrige", false));

    private static TestDraftRequest Request(int questions, params string[] chapters) =>
        TestDraftRequest.For(TestDraftVersions.WithTextChapters(questions, chapters)).Value;

    private static TestDraftAnswerQuestion WithOptions(int count) =>
        Question(1, TestDraftAnswerType.MultipleChoice, "Welche gelten?", [.. Enumerable.Range(1, count).Select(index => ($"Option {index}", index == 1))]);

    private static string Bad(string kind, int maxLength) => kind switch
    {
        "blank" => " \t\n ",
        "empty" => string.Empty,
        "overlong" => new string('x', maxLength + 1),
        "nul" => "Wann\0?",
        _ => throw new System.Diagnostics.UnreachableException(kind),
    };
}
