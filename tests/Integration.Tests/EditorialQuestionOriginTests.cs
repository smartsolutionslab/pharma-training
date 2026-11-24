using System.Net;
using System.Net.Http.Json;
using static SmartSolutionsLab.PharmaTraining.Integration.Tests.EditorialWorkflow;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// Every question records how it came into its version (#245), saved with it and answered through the edge: written by
/// hand, copied from the published version, or drafted by the AI. An AI draft replaces only unreviewed AI-drafted
/// questions, so copied and hand-written ones stay through a settings change, which takes every approval away. This
/// run has no model (ADR 0001): the draft answers 503 and changes nothing; the draft itself is proven with a stand-in
/// model in ApplyAiTestDraftTests and QuestionOriginTests.
/// </summary>
[Collection(EditorialDrafts.Name)]
public class EditorialQuestionOriginTests(AspireFixture aspire)
{
    [Fact]
    public async Task A_question_an_editor_writes_is_answered_as_hand_written()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("editor");
        var draft = await ReadyToSubmitAsync(client, cancellationToken);

        var version = await ReadQuestionsAsync(client, draft.Id, cancellationToken);

        var question = version.Questions.ShouldHaveSingleItem();
        question.Id.ShouldBe(draft.Question);
        question.Origin.ShouldBe("HandWritten");
        question.Provenance.ShouldBe("Human");
    }

    [Fact]
    public async Task After_a_settings_change_the_next_versions_copied_and_hand_written_questions_stay_with_their_origin()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("editor");
        var first = await PublishedAsync(client, cancellationToken);
        var id = await DraftNextAsync(client, first.Id, cancellationToken);
        var copied = await ReadQuestionsAsync(client, id, cancellationToken);
        copied.Questions.ShouldNotBeEmpty();
        copied.Questions.ShouldAllBe(question => question.Origin == "Copied" && question.Review == "Approved");
        var chapter = copied.Questions[0].ChapterId;

        Guid handWritten;
        using (var added = await client.PostAsJsonAsync(Path(id, "questions"), HandWrittenQuestion(chapter), cancellationToken))
        {
            added.StatusCode.ShouldBe(HttpStatusCode.Created, $"POST {Path(id, "questions")} answered {(int)added.StatusCode} {await added.Content.ReadAsStringAsync(cancellationToken)}");
            handWritten = await added.Content.ReadFromJsonAsync<Guid>(cancellationToken);
        }

        var blueprint = new { questions = copied.Questions.Count + 2, passingScorePercent = 90, validityMonths = 12 };
        using (var set = await client.PutAsJsonAsync(Path(id, "blueprint"), blueprint, cancellationToken))
        {
            set.StatusCode.ShouldBe(HttpStatusCode.OK, $"PUT {Path(id, "blueprint")} answered {(int)set.StatusCode}.");
        }

        using (var drafted = await client.PostAsync(Path(id, "test-draft"), null, cancellationToken))
        {
            drafted.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable, "this run has no model, so the draft is reached and changes nothing");
        }

        var changed = await ReadQuestionsAsync(client, id, cancellationToken);
        changed.Questions.Select(question => question.Id).ShouldBe([.. copied.Questions.Select(question => question.Id), handWritten]);
        changed.Questions.Select(question => question.Origin).ShouldBe([.. copied.Questions.Select(_ => "Copied"), "HandWritten"]);
        changed.Questions.ShouldAllBe(question => question.Review == "Unreviewed");
        changed.Questions.Take(copied.Questions.Count).Select(question => question.Provenance).ShouldBe(copied.Questions.Select(question => question.Provenance));
        var published = await ReadQuestionsAsync(client, first.Id, cancellationToken);
        published.Questions.ShouldAllBe(question => question.Origin == "HandWritten");
    }

    private static Uri Path(Guid id, string path) => new($"/editorial/course-versions/{id}/{path}", UriKind.Relative);

    private static object HandWrittenQuestion(Guid chapter) =>
        new
        {
            text = "Wie lange desinfizieren Sie die Hände?",
            type = "SingleChoice",
            chapterId = chapter,
            options = new object[] { new { text = "30 Sekunden", correct = true }, new { text = "5 Sekunden", correct = false } },
        };

    private static async Task<OriginVersion> ReadQuestionsAsync(HttpClient client, Guid id, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(new Uri($"/editorial/course-versions/{id}", UriKind.Relative), cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, $"GET /editorial/course-versions/{id} answered {(int)response.StatusCode}.");
        var version = await response.Content.ReadFromJsonAsync<OriginVersion>(cancellationToken);
        version.ShouldNotBeNull();
        return version;
    }

    private sealed record OriginVersion(Guid Id, IReadOnlyList<OriginQuestion> Questions);

    private sealed record OriginQuestion(Guid Id, Guid ChapterId, string Provenance, string? Origin, string Review);
}
