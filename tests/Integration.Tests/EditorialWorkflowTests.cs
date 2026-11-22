using System.Net;
using System.Net.Http.Json;
using static SmartSolutionsLab.PharmaTraining.Integration.Tests.EditorialWorkflow;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// The editorial workflow through the edge: an editor submits a complete draft for review, returns it to draft,
/// publishes it once every question is approved, and retires it; publishing a course's next version supersedes the
/// previous one, which keeps its content.
/// </summary>
[Collection(EditorialDrafts.Name)]
public class EditorialWorkflowTests(AspireFixture aspire)
{
    [Fact]
    public async Task An_editor_submits_a_complete_draft_and_it_is_listed_in_review()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("editor");
        var draft = await ReadyToSubmitAsync(client, cancellationToken);

        using var submitted = await StepAsync(client, draft.Id, "submit", cancellationToken);

        submitted.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ReadAsync(client, draft.Id, cancellationToken)).Status.ShouldBe("InReview");
        (await IsListedAsync(client, "InReview", draft.Id, cancellationToken)).ShouldBeTrue();
        (await IsListedAsync(client, "Draft", draft.Id, cancellationToken)).ShouldBeFalse();
    }

    [Fact]
    public async Task A_draft_without_chapters_or_with_an_incomplete_test_is_refused()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("editor");
        using var created = await client.PostAsJsonAsync(new Uri("/editorial/courses", UriKind.Relative), new { title = $"Hygiene {Guid.CreateVersion7()}" }, cancellationToken);
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var id = await created.Content.ReadFromJsonAsync<Guid>(cancellationToken);

        using var withoutChapters = await StepAsync(client, id, "submit", cancellationToken);
        withoutChapters.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await CodeOfAsync(withoutChapters, cancellationToken)).ShouldBe("editorial.no-chapters");

        await AddChapterAsync(client, id, cancellationToken);
        using var incomplete = await StepAsync(client, id, "submit", cancellationToken);
        incomplete.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await CodeOfAsync(incomplete, cancellationToken)).ShouldBe("editorial.test-incomplete");

        (await ReadAsync(client, id, cancellationToken)).Status.ShouldBe("Draft");
    }

    [Fact]
    public async Task A_submitted_version_refuses_chapter_changes_until_it_is_returned()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("editor");
        var draft = await ReadyToSubmitAsync(client, cancellationToken);
        await SubmitAsync(client, draft.Id, cancellationToken);
        var chapters = new Uri($"/editorial/course-versions/{draft.Id}/chapters", UriKind.Relative);

        using var refused = await client.PostAsJsonAsync(chapters, new { title = "Flächendesinfektion", text = "Wischen." }, cancellationToken);
        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await CodeOfAsync(refused, cancellationToken)).ShouldBe("editorial.not-a-draft");

        using var returned = await StepAsync(client, draft.Id, "return", cancellationToken);
        returned.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ReadAsync(client, draft.Id, cancellationToken)).Status.ShouldBe("Draft");

        using var added = await client.PostAsJsonAsync(chapters, new { title = "Flächendesinfektion", text = "Wischen." }, cancellationToken);
        added.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Theory]
    [InlineData("submit")]
    [InlineData("return")]
    public async Task Submit_and_return_of_the_empty_guid_are_not_found(string step)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("editor");

        using var response = await StepAsync(client, Guid.Empty, step, cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CodeOfAsync(response, cancellationToken)).ShouldBe("editorial.course-version-not-found");
    }
}
