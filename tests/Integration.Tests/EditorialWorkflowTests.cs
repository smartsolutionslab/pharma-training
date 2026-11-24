using System.Net;
using System.Net.Http.Json;
using System.Text;
using Aspire.Hosting.Testing;
using Npgsql;
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

    [Fact]
    public async Task A_return_without_a_valid_reason_is_refused_and_the_version_stays_in_review()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("editor");
        var draft = await ReadyToSubmitAsync(client, cancellationToken);
        await SubmitAsync(client, draft.Id, cancellationToken);

        using var refused = await client.PostAsJsonAsync(
            new Uri($"/editorial/course-versions/{draft.Id}/return", UriKind.Relative), new { reason = "   " }, cancellationToken);

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await refused.Content.ReadAsStringAsync(cancellationToken));
        (await CodeOfAsync(refused, cancellationToken)).ShouldBe("editorial.invalid-return-reason");
        (await ReadAsync(client, draft.Id, cancellationToken)).Status.ShouldBe("InReview");
    }

    /// <summary>
    /// A return's body that is no JSON is refused with the module's code like a blank reason, not by routing (#181, S1,
    /// ADR 0013): a reason sent as text/plain answers 400, and the version stays in review.
    /// </summary>
    [Fact]
    public async Task A_return_sent_as_plain_text_is_refused_as_an_invalid_reason()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("editor");
        var draft = await ReadyToSubmitAsync(client, cancellationToken);
        await SubmitAsync(client, draft.Id, cancellationToken);
        using var body = new StringContent("""{"reason":"x"}""", Encoding.UTF8, "text/plain");

        using var refused = await client.PostAsync(new Uri($"/editorial/course-versions/{draft.Id}/return", UriKind.Relative), body, cancellationToken);

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await refused.Content.ReadAsStringAsync(cancellationToken));
        (await CodeOfAsync(refused, cancellationToken)).ShouldBe("editorial.invalid-return-reason");
        (await ReadAsync(client, draft.Id, cancellationToken)).Status.ShouldBe("InReview");
    }

    /// <summary>A return's JSON body without a content type is refused with the module's code too.</summary>
    [Fact]
    public async Task A_return_without_a_content_type_is_refused_as_an_invalid_reason()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("editor");
        var draft = await ReadyToSubmitAsync(client, cancellationToken);
        await SubmitAsync(client, draft.Id, cancellationToken);
        using var body = new ByteArrayContent(Encoding.UTF8.GetBytes("""{"reason":"Kapitel 2 braucht eine Quelle."}"""));
        body.Headers.ContentType.ShouldBeNull();

        using var refused = await client.PostAsync(new Uri($"/editorial/course-versions/{draft.Id}/return", UriKind.Relative), body, cancellationToken);

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await refused.Content.ReadAsStringAsync(cancellationToken));
        (await CodeOfAsync(refused, cancellationToken)).ShouldBe("editorial.invalid-return-reason");
        (await ReadAsync(client, draft.Id, cancellationToken)).Status.ShouldBe("InReview");
    }

    /// <summary>Authorization comes before the body: a caller without a token sending text/plain is unauthorized.</summary>
    [Fact]
    public async Task An_anonymous_return_sent_as_plain_text_is_unauthorized()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = aspire.CreateHostClient();
        using var body = new StringContent("""{"reason":"x"}""", Encoding.UTF8, "text/plain");

        using var response = await client.PostAsync(new Uri($"/editorial/course-versions/{Guid.CreateVersion7()}/return", UriKind.Relative), body, cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, await response.Content.ReadAsStringAsync(cancellationToken));
    }

    /// <summary>Authorization comes before the body: a pharmacy owner, no editor, sending text/plain is forbidden.</summary>
    [Fact]
    public async Task A_pharmacy_owners_return_sent_as_plain_text_is_forbidden()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("owner");
        using var body = new StringContent("""{"reason":"x"}""", Encoding.UTF8, "text/plain");

        using var response = await client.PostAsync(new Uri($"/editorial/course-versions/{Guid.CreateVersion7()}/return", UriKind.Relative), body, cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden, await response.Content.ReadAsStringAsync(cancellationToken));
    }

    /// <summary>The return's body is capped at 16 384 bytes, well above any valid reason: one byte more is refused as too large.</summary>
    [Fact]
    public async Task A_return_body_larger_than_the_limit_is_refused_as_too_large()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("editor");
        var draft = await ReadyToSubmitAsync(client, cancellationToken);
        await SubmitAsync(client, draft.Id, cancellationToken);
        var prefix = "{\"reason\":\"";
        var suffix = "\"}";
        var json = prefix + new string('a', 16_385 - prefix.Length - suffix.Length) + suffix;
        Encoding.UTF8.GetByteCount(json).ShouldBe(16_385);
        using var body = new StringContent(json, Encoding.UTF8, "application/json");

        using var refused = await client.PostAsync(new Uri($"/editorial/course-versions/{draft.Id}/return", UriKind.Relative), body, cancellationToken);

        refused.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
        (await ReadAsync(client, draft.Id, cancellationToken)).Status.ShouldBe("InReview");
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

    [Fact]
    public async Task An_editor_publishes_a_reviewed_version()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("editor");
        using var publisher = await aspire.CreateHostClientAsync(Publisher);
        var draft = await ReadyToSubmitAsync(client, cancellationToken);
        await SubmitAsync(client, draft.Id, cancellationToken);
        await ApproveAllAsync(client, draft.Id, cancellationToken);

        using var published = await StepAsync(publisher, draft.Id, "publish", cancellationToken);

        published.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ReadAsync(client, draft.Id, cancellationToken)).Status.ShouldBe("Published");
        (await IsListedAsync(client, "Published", draft.Id, cancellationToken)).ShouldBeTrue();
    }

    [Fact]
    public async Task A_version_with_an_unapproved_question_is_not_published()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("editor");
        using var publisher = await aspire.CreateHostClientAsync(Publisher);
        var draft = await ReadyToSubmitAsync(client, cancellationToken);
        await SubmitAsync(client, draft.Id, cancellationToken);

        using var refused = await StepAsync(publisher, draft.Id, "publish", cancellationToken);

        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await CodeOfAsync(refused, cancellationToken)).ShouldBe("editorial.question-not-approved");
        (await ReadAsync(client, draft.Id, cancellationToken)).Status.ShouldBe("InReview");
    }

    [Fact]
    public async Task A_version_whose_settings_changed_after_approval_is_not_published_until_its_questions_are_approved_again()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("editor");
        using var publisher = await aspire.CreateHostClientAsync(Publisher);
        var draft = await ReadyToSubmitAsync(client, cancellationToken);
        await ApproveAllAsync(client, draft.Id, cancellationToken);
        var path = new Uri($"/editorial/course-versions/{draft.Id}/blueprint", UriKind.Relative);
        using (var set = await client.PutAsJsonAsync(path, new { questions = 1, passingScorePercent = 90, validityMonths = 12 }, cancellationToken))
        {
            set.StatusCode.ShouldBe(HttpStatusCode.OK, $"PUT {path} answered {(int)set.StatusCode}.");
        }

        await SubmitAsync(client, draft.Id, cancellationToken);

        using (var refused = await StepAsync(publisher, draft.Id, "publish", cancellationToken))
        {
            refused.StatusCode.ShouldBe(HttpStatusCode.Conflict, "a version whose settings changed after its questions were approved was published unreviewed");
            (await CodeOfAsync(refused, cancellationToken)).ShouldBe("editorial.question-not-approved");
        }

        (await ReadAsync(client, draft.Id, cancellationToken)).Status.ShouldBe("InReview");
        await ApproveAllAsync(client, draft.Id, cancellationToken);
        using var published = await StepAsync(publisher, draft.Id, "publish", cancellationToken);
        published.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ReadAsync(client, draft.Id, cancellationToken)).Status.ShouldBe("Published");
    }

    [Fact]
    public async Task A_published_version_is_not_published_again()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("editor");
        using var publisher = await aspire.CreateHostClientAsync(Publisher);
        var draft = await PublishedAsync(client, publisher, cancellationToken);

        using var refused = await StepAsync(client, draft.Id, "publish", cancellationToken);

        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await CodeOfAsync(refused, cancellationToken)).ShouldBe("editorial.not-in-review");
        (await ReadAsync(client, draft.Id, cancellationToken)).Status.ShouldBe("Published");
    }

    [Fact]
    public async Task Publishing_the_next_version_supersedes_the_previous_one_which_keeps_its_content()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("editor");
        using var publisher = await aspire.CreateHostClientAsync(Publisher);
        var (first, next) = await SupersededAsync(client, publisher, cancellationToken);

        var previous = await ReadAsync(client, first.Id, cancellationToken);
        previous.Status.ShouldBe("Superseded");
        (await ContentOfAsync(client, first.Id, cancellationToken)).ShouldBe(first.Content);
        var current = await ReadAsync(client, next.Id, cancellationToken);
        current.Status.ShouldBe("Published");
        current.CourseId.ShouldBe(previous.CourseId);
        current.Number.ShouldBe(2);
        (await IsListedAsync(client, "Published", next.Id, cancellationToken)).ShouldBeTrue();
        (await IsListedAsync(client, "Published", first.Id, cancellationToken)).ShouldBeFalse();
        (await IsListedAsync(client, "Superseded", first.Id, cancellationToken)).ShouldBeTrue();
    }

    /// <summary>
    /// The run uses four eyes (ADR 0007, #246): the editor who submitted the version may not publish it. The default, four
    /// eyes off, is the unit tests' of the domain, the publish handler and EditorialSettings.
    /// </summary>
    [Fact]
    public async Task The_submitter_cannot_publish_under_four_eyes()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var submitter = await aspire.CreateHostClientAsync("editor-b");
        using var reviewer = await aspire.CreateHostClientAsync("editor");
        var draft = await ReadyToSubmitAsync(submitter, cancellationToken);
        await SubmitAsync(submitter, draft.Id, cancellationToken);
        await ApproveAllAsync(reviewer, draft.Id, cancellationToken);

        using var refused = await StepAsync(submitter, draft.Id, "publish", cancellationToken);

        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden, await refused.Content.ReadAsStringAsync(cancellationToken));
        (await CodeOfAsync(refused, cancellationToken)).ShouldBe("editorial.four-eyes");
        (await ReadAsync(submitter, draft.Id, cancellationToken)).Status.ShouldBe("InReview");
    }

    [Fact]
    public async Task An_editor_retires_a_published_version_and_the_catalogue_hears_of_it()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("editor");
        using var publisher = await aspire.CreateHostClientAsync(Publisher);
        var draft = await PublishedAsync(client, publisher, cancellationToken);

        using var retired = await StepAsync(client, draft.Id, "retire", cancellationToken);

        retired.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ReadAsync(client, draft.Id, cancellationToken)).Status.ShouldBe("Retired");
        await WaitUntilCatalogHoldsRetirementAsync(draft.Id, draft.CourseId, cancellationToken);
    }

    [Fact]
    public async Task A_draft_is_not_retired()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("editor");
        var draft = await ReadyToSubmitAsync(client, cancellationToken);

        using var refused = await StepAsync(client, draft.Id, "retire", cancellationToken);

        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await CodeOfAsync(refused, cancellationToken)).ShouldBe("editorial.not-published");
        (await ReadAsync(client, draft.Id, cancellationToken)).Status.ShouldBe("Draft");
    }

    [Fact]
    public async Task A_superseded_version_stays_retirable()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("editor");
        using var publisher = await aspire.CreateHostClientAsync(Publisher);
        var (first, next) = await SupersededAsync(client, publisher, cancellationToken);

        using var retired = await StepAsync(client, first.Id, "retire", cancellationToken);

        retired.StatusCode.ShouldBe(HttpStatusCode.OK, await retired.Content.ReadAsStringAsync(cancellationToken));
        (await ReadAsync(client, first.Id, cancellationToken)).Status.ShouldBe("Retired");
        (await ContentOfAsync(client, first.Id, cancellationToken)).ShouldBe(first.Content);
        (await ReadAsync(client, next.Id, cancellationToken)).Status.ShouldBe("Published");
    }

    [Theory]
    [InlineData("publish")]
    [InlineData("retire")]
    public async Task Publish_and_retire_of_the_empty_guid_are_not_found(string step)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("editor");

        using var response = await StepAsync(client, Guid.Empty, step, cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CodeOfAsync(response, cancellationToken)).ShouldBe("editorial.course-version-not-found");
    }

    /// <summary>
    /// Publishes a course's first version, drafts its next one through the API, replaces the copied chapter's text so
    /// its question needs approval again, and publishes it. Answers the first version with its content as read before,
    /// and the second.
    /// </summary>
    private static async Task<(Published First, Published Next)> SupersededAsync(HttpClient client, HttpClient publisher, CancellationToken cancellationToken)
    {
        var first = await PublishedAsync(client, publisher, cancellationToken);
        var firstContent = await ContentOfAsync(client, first.Id, cancellationToken);
        var nextId = await DraftNextAsync(client, first.Id, cancellationToken);
        var chapter = new Uri($"/editorial/course-versions/{nextId}/chapters/{(await ReadChaptersAsync(client, nextId, cancellationToken)).Single()}", UriKind.Relative);
        using (var replaced = await client.PutAsJsonAsync(chapter, new { title = "Händehygiene", text = "Neu: vor und nach …" }, cancellationToken))
        {
            replaced.StatusCode.ShouldBe(HttpStatusCode.OK, $"PUT {chapter} answered {(int)replaced.StatusCode}.");
        }

        await SubmitAsync(client, nextId, cancellationToken);
        await ApproveAllAsync(client, nextId, cancellationToken);
        await PublishAsync(publisher, nextId, cancellationToken);
        return (new Published(first.Id, firstContent), new Published(nextId, await ContentOfAsync(client, nextId, cancellationToken)));
    }

    /// <summary>The ids of the version's chapters, in order.</summary>
    private static async Task<IReadOnlyList<Guid>> ReadChaptersAsync(HttpClient client, Guid id, CancellationToken cancellationToken)
    {
        var version = await client.GetFromJsonAsync<VersionChapters>(new Uri($"/editorial/course-versions/{id}", UriKind.Relative), cancellationToken);
        version.ShouldNotBeNull();
        return [.. version.Chapters.Select(chapter => chapter.Id)];
    }

    private sealed record Published(Guid Id, string Content);

    private sealed record VersionChapters(IReadOnlyList<VersionChapter> Chapters);

    private sealed record VersionChapter(Guid Id);

    /// <summary>
    /// Polls Catalog's database until it holds the version's retirement, which only <c>CourseVersionRetiredV1</c> from
    /// Editorial puts there, and fails the test when it does not within 30 seconds.
    /// </summary>
    private async Task WaitUntilCatalogHoldsRetirementAsync(Guid versionId, Guid courseId, CancellationToken cancellationToken)
    {
        var connectionString = await aspire.App.GetConnectionStringAsync("catalog-database", cancellationToken)
            ?? throw new InvalidOperationException("Catalog's database has no connection string.");
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        var started = TimeProvider.System.GetTimestamp();
        while (true)
        {
            await using var command = new NpgsqlCommand("select course_id from catalog.course_version_retirements where course_version_id = @id", connection);
            command.Parameters.AddWithValue("id", versionId);
            if (await command.ExecuteScalarAsync(cancellationToken) is Guid retiredCourse)
            {
                retiredCourse.ShouldBe(courseId);
                return;
            }

            TimeProvider.System.GetElapsedTime(started).ShouldBeLessThan(TimeSpan.FromSeconds(30), $"Catalog never heard that version {versionId} was retired.");
            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
        }
    }
}
