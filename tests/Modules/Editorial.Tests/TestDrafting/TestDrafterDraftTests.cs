using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.TestDrafting;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;
using SmartSolutionsLab.PharmaTraining.Tests.Fakes;
using static SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions.CourseVersionBuilder;
using static SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.TestDrafting.DraftAnswers;
using static SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.TestDrafting.TestDrafterAskTests;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.TestDrafting;

/// <summary>
/// The whole draft as #22's handler runs it: the request from the version, the model's answer, and the drafted
/// questions, which apply to the version and fill its blueprint. A version that cannot be drafted never reaches the model.
/// </summary>
public class TestDrafterDraftTests
{
    [Theory]
    [InlineData("not a draft", "editorial.not-a-draft")]
    [InlineData("no text chapters", "editorial.no-text-chapters")]
    [InlineData("nothing to draft", "editorial.nothing-to-draft")]
    [InlineData("too much content", "editorial.too-much-content-to-draft")]
    public async Task Draft_refuses_before_asking_the_model_when_the_version_cannot_be_drafted(string state, string code)
    {
        var version = Undraftable(state);
        using var model = new FakeChatClient(Sound(1));

        var result = await TestDrafter.DraftAsync(model, version, TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(code);
        model.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Draft_returns_the_drafted_questions_of_a_sound_answer()
    {
        var version = TestDraftVersions.WithTextChapters(3, "Händehygiene", "Flächen");
        var chapters = TestDraftVersions.TextChapterIds(version);
        using var model = new FakeChatClient(Sound(1, 2, 1));

        var result = await TestDrafter.DraftAsync(model, version, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Select(question => question.Chapter).ShouldBe([chapters[0], chapters[1], chapters[0]]);
        model.Requests.ShouldHaveSingleItem();
    }

    [Theory]
    [InlineData("An invoice records what was sold.", nameof(TestDraftDefect.Unreadable))]
    [InlineData("""{"questions":[]}""", nameof(TestDraftDefect.WrongCount))]
    public async Task Draft_reports_a_malformed_answer_and_returns_no_questions(string answer, string defect)
    {
        var version = TestDraftVersions.WithTextChapters(2, "Händehygiene");
        using var model = new FakeChatClient(answer);

        var result = await TestDrafter.DraftAsync(model, version, TestContext.Current.CancellationToken);

        Malformed(result, Enum.Parse<TestDraftDefect>(defect));
        version.Questions.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_drafted_questions_apply_to_their_version_and_fill_its_blueprint()
    {
        var version = TestDraftVersions.WithTextChapters(5, "Händehygiene", "Flächen");
        var chapter = TestDraftVersions.TextChapterIds(version)[0];
        var approved = version.AddQuestion(QuestionId.New(), SingleChoice(chapter), AnswerOptionId.New).Value;
        var otherApproved = version.AddQuestion(QuestionId.New(), SingleChoice(chapter), AnswerOptionId.New).Value;
        var unreviewed = version.AddQuestion(QuestionId.New(), SingleChoice(chapter), AnswerOptionId.New).Value;
        version.ApproveQuestion(approved, Author, Now).IsSuccess.ShouldBeTrue();
        version.ApproveQuestion(otherApproved, Author, Now).IsSuccess.ShouldBeTrue();
        using var model = new FakeChatClient(DraftAnswers.Sound(1, 1, 2));

        var drafted = (await TestDrafter.DraftAsync(model, version, TestContext.Current.CancellationToken)).Value;
        var applied = version.ApplyAiTestDraft(drafted, QuestionId.New, AnswerOptionId.New);

        applied.IsSuccess.ShouldBeTrue();
        version.Questions.Count.ShouldBe(version.Blueprint.Questions.Value);
        version.Questions.Where(question => question.Review == ReviewState.Approved).Select(question => question.Id).ShouldBe([approved, otherApproved]);
        version.Questions.ShouldNotContain(question => question.Id == unreviewed);
        var added = version.Questions.Where(question => question.Id != approved && question.Id != otherApproved).ToList();
        added.Count.ShouldBe(3);
        added.ShouldAllBe(question => question.Provenance == Provenance.AiDrafted && question.Review == ReviewState.Unreviewed);
    }

    private static CourseVersion Undraftable(string state) => state switch
    {
        "not a draft" => InReview(),
        "no text chapters" => Draft(),
        "nothing to draft" => Full(),
        "too much content" => TooLong(),
        _ => throw new System.Diagnostics.UnreachableException(state),
    };

    private static CourseVersion Full()
    {
        var version = TestDraftVersions.WithTextChapters(1, "Händehygiene");
        var chapter = TestDraftVersions.TextChapterIds(version)[0];
        var question = version.AddQuestion(QuestionId.New(), SingleChoice(chapter), AnswerOptionId.New).Value;
        version.ApproveQuestion(question, Author, Now).IsSuccess.ShouldBeTrue();
        return version;
    }

    private static CourseVersion TooLong()
    {
        var version = Draft();
        version.AddChapter(TextChapter(ChapterId.New(), "A", new string('a', MarkdownText.MaxLength))).IsSuccess.ShouldBeTrue();
        version.AddChapter(TextChapter(ChapterId.New(), "B", new string('b', MarkdownText.MaxLength))).IsSuccess.ShouldBeTrue();
        return version;
    }
}
