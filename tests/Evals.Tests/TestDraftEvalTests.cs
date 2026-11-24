using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.TestDrafting;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;
using SmartSolutionsLab.PharmaTraining.Tests.Fakes;

namespace SmartSolutionsLab.PharmaTraining.Evals.Tests;

/// <summary>
/// The AI test draft's use case in the evals: its examples, answered through the module's drafter, and the project's
/// own draft metrics, scored without a judge from what the drafter read. The model is a stand-in answering hand-written
/// JSON for the first example (three text chapters, six questions, two each); the judge is the fake one. No test calls
/// a model.
/// </summary>
public sealed partial class TestDraftEvalTests : IDisposable
{
    private const string UseCaseName = "ai-test-draft";

    private const string ChapterReferenced = "Chapter referenced";
    private const string CountMatches = "Count matches blueprint";
    private const string SingleChoiceOneCorrect = "Single choice has one correct";
    private const string EvenlyDistributed = "Evenly distributed";
    private const string Accepted = "Accepted";

    private const string ChaptersOpen = "<chapters>";
    private const string ChaptersClose = "</chapters>";

    private static readonly string[] DraftMetrics = [ChapterReferenced, CountMatches, SingleChoiceOneCorrect, EvenlyDistributed, Accepted];

    private readonly DirectoryInfo results = Directory.CreateTempSubdirectory("evals-draft-");

    [Fact]
    public void The_invoice_sample_is_replaced_by_the_ai_test_draft()
    {
        var names = UseCases.All.Select(useCase => useCase.Name).ToList();

        names.ShouldContain(UseCaseName);
        names.ShouldNotContain("ask-the-model");
    }

    [Fact]
    public async Task A_sound_draft_scores_5_on_every_draft_metric()
    {
        var scores = await RunFirstExampleAsync(Answer(Sound(1), Sound(1), Sound(2), Sound(2), Sound(3), Sound(3)));

        foreach (var metric in DraftMetrics)
        {
            Score(scores, metric).ShouldBe(5, metric);
        }

        scores.BelowThreshold(4).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_wrong_count_scores_1_on_count_and_accepted()
    {
        var scores = await RunFirstExampleAsync(Answer(Sound(1), Sound(1), Sound(2), Sound(2), Sound(3)));

        Score(scores, CountMatches).ShouldBe(1);
        Score(scores, Accepted).ShouldBe(1);
        Score(scores, ChapterReferenced).ShouldBe(5);
    }

    [Fact]
    public async Task A_question_on_a_chapter_the_version_lacks_lowers_chapter_referenced_and_is_not_accepted()
    {
        var scores = await RunFirstExampleAsync(Answer(Sound(1), Sound(1), Sound(2), Sound(2), Sound(3), Sound(4)));

        Score(scores, ChapterReferenced).ShouldNotBeNull().ShouldBe(1 + (4.0 * 5 / 6), 0.001);
        Score(scores, Accepted).ShouldBe(1);
        Score(scores, CountMatches).ShouldBe(5);
    }

    [Fact]
    public async Task A_single_choice_question_with_two_correct_options_lowers_its_metric_but_is_accepted()
    {
        var twoCorrect = Question(3, "SingleChoice", "Was gilt laut Kapitel 3?", ("Das Richtige", true), ("Auch richtig", true), ("Das Falsche", false), ("Das Andere", false));

        var scores = await RunFirstExampleAsync(Answer(Sound(1), Sound(1), Sound(2), Sound(2), Sound(3), twoCorrect));

        Score(scores, SingleChoiceOneCorrect).ShouldNotBeNull().ShouldBe(1 + (4.0 * 5 / 6), 0.001);
        Score(scores, Accepted).ShouldBe(5);
        Score(scores, ChapterReferenced).ShouldBe(5);
        Score(scores, CountMatches).ShouldBe(5);
        Score(scores, EvenlyDistributed).ShouldBe(5);
    }

    [Fact]
    public async Task An_uneven_split_lowers_evenly_distributed_only()
    {
        var scores = await RunFirstExampleAsync(Answer(Sound(1), Sound(1), Sound(1), Sound(2), Sound(2), Sound(3)));

        Score(scores, EvenlyDistributed).ShouldNotBeNull().ShouldBe(1 + (4.0 * 1 / 3), 0.001);
        foreach (var metric in DraftMetrics.Where(metric => metric != EvenlyDistributed))
        {
            Score(scores, metric).ShouldBe(5, metric);
        }
    }

    [Theory]
    [InlineData("An invoice records what was sold, to whom and for how much.")]
    [InlineData("")]
    [InlineData("""{"questions":[null]}""")]
    public async Task An_unreadable_answer_scores_1_on_every_draft_metric(string answer)
    {
        var scores = await RunFirstExampleAsync(answer);

        foreach (var metric in DraftMetrics)
        {
            Score(scores, metric).ShouldBe(1, metric);
        }
    }

    [Fact]
    public async Task An_answer_of_another_use_case_gets_no_draft_metric()
    {
        var fixture = new EvalUseCase("fixture", (model, example, cancellationToken) => model.GetResponseAsync(EvalPrompt.Messages(example), cancellationToken: cancellationToken), [new("What does an invoice record?")]);
        using var model = new FakeChatClient("An invoice records what was sold, to whom and for how much.");
        using var judge = new FakeJudge(5);

        var scores = await EvalRun.RunAsync([fixture], model, judge, results.FullName, "draft", TestContext.Current.CancellationToken);

        scores.All.ShouldNotBeEmpty();
        scores.All.ShouldNotContain(score => DraftMetrics.Contains(score.Metric));
    }

    [Fact]
    public async Task Every_draft_example_has_grounding_context_and_its_request_carries_no_guid()
    {
        var draft = DraftUseCase();
        draft.Examples.ShouldNotBeEmpty();
        draft.Examples.Select(example => example.Input).ShouldBeUnique();

        foreach (var example in draft.Examples)
        {
            using var model = new FakeChatClient("{}");

            await draft.Answer(model, example, TestContext.Current.CancellationToken);

            var groundingContext = example.GroundingContext.ShouldNotBeNull(example.Input);
            groundingContext.ShouldNotBeNullOrWhiteSpace(example.Input);
            var messages = model.Requests.ShouldHaveSingleItem(example.Input);
            var sent = string.Join("\n", messages.Select(message => message.Text));
            GuidPattern().IsMatch(sent).ShouldBeFalse(example.Input);
            foreach (var chapter in Chapters(messages))
            {
                groundingContext.ShouldContain(chapter["title"]!.GetValue<string>(), Case.Sensitive, example.Input);
                groundingContext.ShouldContain(chapter["text"]!.GetValue<string>(), Case.Sensitive, example.Input);
            }
        }
    }

    [Fact]
    public async Task The_draft_examples_request_the_counts_of_their_blueprints_less_the_approved_questions()
    {
        var draft = DraftUseCase();
        List<int> requested = [];

        foreach (var example in draft.Examples)
        {
            using var model = new FakeChatClient("{}");
            await draft.Answer(model, example, TestContext.Current.CancellationToken);
            var user = model.Requests.ShouldHaveSingleItem().Single(message => message.Role == ChatRole.User).Text;
            requested.Add(int.Parse(RequestedPattern().Match(user).Groups[1].Value, CultureInfo.InvariantCulture));
        }

        requested.ShouldBe([6, 10, 5, 4, 7, 8, 5]);
    }

    [Fact]
    public async Task The_draft_use_case_answers_through_the_modules_drafter_with_the_instruction_alone_in_the_system_message()
    {
        var draft = DraftUseCase();
        var questions = new[] { Sound(1), Sound(1), Sound(2), Sound(2), Sound(3), Sound(3) };
        using var model = new FakeChatClient(Answer(questions));

        var response = await draft.Answer(model, draft.Examples[0], TestContext.Current.CancellationToken);

        var messages = model.Requests.ShouldHaveSingleItem();
        messages.Single(message => message.Role == ChatRole.System).Text.ShouldBe(TestDraftPrompt.Instruction);
        var user = messages.Single(message => message.Role == ChatRole.User).Text;
        user.ShouldContain(ChaptersOpen);
        user.ShouldContain(ChaptersClose);
        model.Options.ShouldHaveSingleItem().ShouldNotBeNull().ResponseFormat.ShouldBeOfType<ChatResponseFormatJson>();
        response.Text.ShouldNotBeNullOrWhiteSpace();
        foreach (var question in questions)
        {
            response.Text.ShouldContain(question["text"]!.GetValue<string>());
        }

        var transcript = response.AdditionalProperties.ShouldNotBeNull()["pharma.test-draft"].ShouldBeOfType<string>();
        Should.NotThrow(() => JsonDocument.Parse(transcript).Dispose());
    }

    [Fact]
    public void Every_draft_example_builds_a_version_the_drafter_accepts()
    {
        foreach (var example in DraftUseCase().Examples)
        {
            var request = TestDraftRequest.For(TestDraftExamples.Version(example));

            request.IsSuccess.ShouldBeTrue(request.IsFailure ? $"{example.Input}: {request.Error.Code}" : example.Input);
        }
    }

    [Fact]
    public async Task The_zytostatika_example_sends_text_and_pdf_chapters_split_evenly_and_leaves_out_its_video()
    {
        var example = Example("Lagerung von Zytostatika");
        var version = TestDraftExamples.Version(example);
        using var model = new FakeChatClient("{}");

        await DraftUseCase().Answer(model, example, TestContext.Current.CancellationToken);

        var messages = model.Requests.ShouldHaveSingleItem();
        Chapters(messages).Select(chapter => chapter["source"]!.GetValue<string>()).ShouldBe(["text", "pdf", "text", "pdf"]);
        var user = messages.Single(message => message.Role == ChatRole.User).Text;
        user.ShouldContain("chapter 1: 2", Case.Insensitive);
        user.ShouldContain("chapter 2: 2", Case.Insensitive);
        user.ShouldContain("chapter 3: 2", Case.Insensitive);
        user.ShouldContain("chapter 4: 2", Case.Insensitive);
        var request = TestDraftRequest.For(version).Value;
        var video = version.Chapters.Where(chapter => chapter.Kind == ChapterKind.Video).ShouldHaveSingleItem();
        request.LeftOutChapters.ShouldBe([video.Id]);
    }

    [Fact]
    public async Task The_ethanol_example_sends_pdf_chapters_only()
    {
        var example = Example("Betriebsanweisung Ethanol (PDF)");
        using var model = new FakeChatClient("{}");

        await DraftUseCase().Answer(model, example, TestContext.Current.CancellationToken);

        var chapters = Chapters(model.Requests.ShouldHaveSingleItem()).ToList();
        chapters.Count.ShouldBe(2);
        chapters.ShouldAllBe(chapter => chapter["source"]!.GetValue<string>() == "pdf");
        TestDraftRequest.For(TestDraftExamples.Version(example)).Value.LeftOutChapters.ShouldBeEmpty();
    }

    [Fact]
    public void The_hazardous_substances_example_keeps_a_pdf_without_text_that_is_left_out()
    {
        var version = TestDraftExamples.Version(Example("Gefahrstoffe in der Rezeptur"));

        var scan = version.Chapters.Where(chapter => chapter.Kind == ChapterKind.Pdf).ShouldHaveSingleItem();
        scan.Extraction.ShouldBe(PdfTextExtraction.NoText);
        TestDraftRequest.For(version).Value.LeftOutChapters.ShouldBe([scan.Id]);
    }

    [Fact]
    public void The_pdf_examples_grounding_context_holds_the_pdf_texts_numbered_among_the_chapters_sent()
    {
        foreach (var title in new[] { "Lagerung von Zytostatika", "Betriebsanweisung Ethanol (PDF)" })
        {
            var example = Example(title);
            var request = TestDraftRequest.For(TestDraftExamples.Version(example)).Value;
            var groundingContext = example.GroundingContext.ShouldNotBeNull(title);

            var pdfs = request.Chapters.Where(chapter => chapter.Source == TestDraftSource.Pdf).ToList();
            pdfs.ShouldNotBeEmpty(title);
            foreach (var chapter in request.Chapters)
            {
                groundingContext.ShouldContain(string.Create(CultureInfo.InvariantCulture, $"Kapitel {chapter.Number}: {chapter.Title.Value}\n\n{chapter.Text}"), Case.Sensitive, title);
            }
        }
    }

    [Fact]
    public void The_pdf_texts_of_the_examples_read_as_pdfpig_yields_them()
    {
        var request = TestDraftRequest.For(TestDraftExamples.Version(Example("Lagerung von Zytostatika"))).Value;

        foreach (var pdf in request.Chapters.Where(chapter => chapter.Source == TestDraftSource.Pdf))
        {
            Regex.Count(pdf.Text, Regex.Escape("Apotheke am Markt · Arbeitsanweisung")).ShouldBeGreaterThan(1, "a header repeated on every page");
            pdf.Text.ShouldContain("\nSeite 1\n\n", Case.Sensitive, "a page number, then the pages joined by a blank line");
            pdf.Text.ShouldMatch("[a-zäöü]-\n[a-zäöü]", "a word hyphenated across a line break");
        }
    }

    public void Dispose() => results.Delete(recursive: true);

    private static EvalUseCase DraftUseCase() => UseCases.All.Single(useCase => useCase.Name == UseCaseName);

    private static EvalExample Example(string course) =>
        DraftUseCase().Examples.Single(example => example.Input.Contains($"\"{course}\"", StringComparison.Ordinal));

    private static double? Score(EvalScores scores, string metric) =>
        scores.All.Where(score => score.UseCase == UseCaseName && score.Metric == metric).ShouldHaveSingleItem(metric).Value;

    private static JsonObject Question(int chapter, string type, string text, params (string Text, bool Correct)[] options) =>
        new()
        {
            ["chapter"] = chapter,
            ["type"] = type,
            ["text"] = text,
            ["options"] = new JsonArray([.. options.Select(option => new JsonObject { ["text"] = option.Text, ["correct"] = option.Correct })]),
        };

    /// <summary>A sound single-choice question about the chapter numbered <paramref name="chapter"/>: four options, one correct.</summary>
    private static JsonObject Sound(int chapter) =>
        Question(chapter, "SingleChoice", $"Was gilt laut Kapitel {chapter}?", ("Das Richtige", true), ("Das Falsche", false), ("Das Andere", false), ("Das Übrige", false));

    private static string Answer(params JsonObject[] questions) =>
        new JsonObject { ["questions"] = new JsonArray([.. questions]) }.ToJsonString();

    private static IEnumerable<JsonObject> Chapters(IReadOnlyList<ChatMessage> messages)
    {
        var user = messages.Single(message => message.Role == ChatRole.User).Text;
        var start = user.IndexOf(ChaptersOpen, StringComparison.Ordinal) + ChaptersOpen.Length;
        var end = user.IndexOf(ChaptersClose, StringComparison.Ordinal);
        return JsonNode.Parse(user[start..end])!.AsArray().Select(chapter => chapter!.AsObject());
    }

    private async Task<EvalScores> RunFirstExampleAsync(string answer)
    {
        var draft = DraftUseCase();
        var firstExample = new EvalUseCase(draft.Name, draft.Answer, [draft.Examples[0]]);
        using var model = new FakeChatClient(answer);
        using var judge = new FakeJudge(5);
        return await EvalRun.RunAsync([firstExample], model, judge, results.FullName, "draft", TestContext.Current.CancellationToken);
    }

    [GeneratedRegex("[0-9a-fA-F]{8}-?[0-9a-fA-F]{4}-?[0-9a-fA-F]{4}-?[0-9a-fA-F]{4}-?[0-9a-fA-F]{12}")]
    private static partial Regex GuidPattern();

    [GeneratedRegex(@"exactly (\d+) questions", RegexOptions.IgnoreCase)]
    private static partial Regex RequestedPattern();
}
