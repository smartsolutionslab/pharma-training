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
        var version = TestDraftVersions.Undraftable(state);
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
        result.Value.Questions.Select(question => question.Chapter).ShouldBe([chapters[0], chapters[1], chapters[0]]);
        model.Requests.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Draft_names_the_pdf_chapters_without_text_and_video_chapters_it_left_out_in_the_versions_order()
    {
        var version = TestDraftVersions.WithTextPdfAndVideoChapters(2, out var leftOutInOrder);
        var texts = TestDraftVersions.TextChapterIds(version);
        using var model = new FakeChatClient(Sound(1, 2));

        var result = await TestDrafter.DraftAsync(model, version, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        var draft = result.Value.ShouldBeOfType<TestDraft>();
        draft.Questions.Select(question => question.Chapter).ShouldBe([texts[0], texts[1]]);
        draft.LeftOutChapters.ShouldBe(leftOutInOrder);
        leftOutInOrder.Count.ShouldBe(3);
    }

    [Fact]
    public async Task Draft_of_a_version_of_pdf_chapters_only_returns_questions_on_those_pdf_chapters()
    {
        var version = TestDraftVersions.WithChapters(3, "PR", out var ids);
        using var model = new FakeChatClient(Sound(1, 2, 1));

        var result = await TestDrafter.DraftAsync(model, version, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Code : string.Empty);
        result.Value.Questions.Select(question => question.Chapter).ShouldBe([ids[0], ids[1], ids[0]]);
        result.Value.LeftOutChapters.ShouldBeEmpty();
        result.Value.TruncatedChapters.ShouldBe([ids[1]]);
    }

    [Fact]
    public async Task An_answer_naming_a_pdf_chapters_number_maps_to_that_pdf_chapter_in_a_mixed_version()
    {
        // Numbers among the drafted chapters: 1 the text chapter, 2 the PDF, 3 the second text chapter; the video and
        // the scan take no number.
        var version = TestDraftVersions.WithChapters(4, "TVPNT", out var ids);
        using var model = new FakeChatClient(Sound(2, 1, 3, 1));

        var result = await TestDrafter.DraftAsync(model, version, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Code : string.Empty);
        result.Value.Questions.Select(question => question.Chapter).ShouldBe([ids[2], ids[0], ids[4], ids[0]]);
        result.Value.LeftOutChapters.ShouldBe([ids[1], ids[3]]);
        result.Value.TruncatedChapters.ShouldBeEmpty();
    }

    [Fact]
    public async Task Draft_names_the_pdf_chapters_it_drafted_from_cut_off_text_in_the_versions_order()
    {
        var version = TestDraftVersions.WithChapters(3, "RTR", out var ids);
        using var model = new FakeChatClient(Sound(1, 2, 3));

        var result = await TestDrafter.DraftAsync(model, version, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Code : string.Empty);
        var draft = result.Value.ShouldBeOfType<TestDraft>();
        draft.TruncatedChapters.ShouldBe([ids[0], ids[2]]);
        draft.LeftOutChapters.ShouldBeEmpty();
    }

    [Fact]
    public async Task Draft_of_a_version_whose_pdfs_have_no_text_refuses_before_asking_the_model()
    {
        var version = TestDraftVersions.WithChapters(2, "NLV", out _);
        using var model = new FakeChatClient(Sound(1, 1));

        var result = await TestDrafter.DraftAsync(model, version, TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("editorial.no-text-chapters");
        model.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Draft_of_a_version_with_text_chapters_only_leaves_no_chapter_out()
    {
        var version = TestDraftVersions.WithTextChapters(2, "Händehygiene", "Flächen");
        using var model = new FakeChatClient(Sound(1, 2));

        var result = await TestDrafter.DraftAsync(model, version, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Questions.Count.ShouldBe(2);
        result.Value.LeftOutChapters.ShouldBeEmpty();
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
    public async Task Draft_refuses_a_chapter_number_written_as_a_string_as_malformed_and_returns_no_questions()
    {
        var version = TestDraftVersions.WithTextChapters(2, "Händehygiene", "Flächen");
        var quoted = SingleChoice(2);
        quoted["chapter"] = "2";
        using var model = new FakeChatClient(Answer(SingleChoice(1), quoted));

        var result = await TestDrafter.DraftAsync(model, version, TestContext.Current.CancellationToken);

        Malformed(result, TestDraftDefect.Unreadable);
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
        version.ApproveQuestion(approved, Author, AuthorsName, Now).IsSuccess.ShouldBeTrue();
        version.ApproveQuestion(otherApproved, Author, AuthorsName, Now).IsSuccess.ShouldBeTrue();
        using var model = new FakeChatClient(DraftAnswers.Sound(1, 1, 2));

        var drafted = (await TestDrafter.DraftAsync(model, version, TestContext.Current.CancellationToken)).Value;
        var applied = version.ApplyAiTestDraft(drafted.Questions, QuestionId.New, AnswerOptionId.New);

        applied.IsSuccess.ShouldBeTrue();
        version.Questions.Count.ShouldBe(version.Blueprint.Questions.Value);
        version.Questions.Where(question => question.Review == ReviewState.Approved).Select(question => question.Id).ShouldBe([approved, otherApproved]);
        version.Questions.ShouldNotContain(question => question.Id == unreviewed);
        var added = version.Questions.Where(question => question.Id != approved && question.Id != otherApproved).ToList();
        added.Count.ShouldBe(3);
        added.ShouldAllBe(question => question.Provenance == Provenance.AiDrafted && question.Review == ReviewState.Unreviewed);
    }
}
