using System.Net;
using System.Net.Http.Json;
using static SmartSolutionsLab.PharmaTraining.Integration.Tests.EditorialWorkflow;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// An editor drafts the next version of a published one through the edge (E7, <c>POST …/{id}/next</c>): a draft of
/// the same course numbered one higher, its content copied with new ids and its questions still approved until they or
/// their chapter change. One successor per version; only a published version has one.
/// </summary>
[Collection(EditorialDrafts.Name)]
public class EditorialNextVersionTests(AspireFixture aspire)
{
    [Fact]
    public async Task An_editor_drafts_the_next_version_of_a_published_one()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("editor");
        var first = await PublishedAsync(client, cancellationToken);

        using var response = await client.PostAsync(NextOf(first.Id), null, cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Created, $"POST {NextOf(first.Id)} answered {(int)response.StatusCode} {await response.Content.ReadAsStringAsync(cancellationToken)}");
        var id = await response.Content.ReadFromJsonAsync<Guid>(cancellationToken);
        id.ShouldNotBe(first.Id);
        response.Headers.Location.ShouldBe(new Uri($"/editorial/course-versions/{id}", UriKind.Relative));
        var previous = await ReadContentAsync(client, first.Id, cancellationToken);
        var next = await ReadContentAsync(client, id, cancellationToken);
        next.Status.ShouldBe("Draft");
        next.CourseId.ShouldBe(first.CourseId);
        next.Number.ShouldBe(2);
        next.Questions.ShouldNotBeEmpty();
        next.Questions.ShouldAllBe(question => question.Review == "Approved");
        next.Questions.Select(question => question.Id).ShouldBeUnique();
        next.Questions.Select(question => question.Id).Intersect(previous.Questions.Select(question => question.Id)).ShouldBeEmpty();
        next.Chapters.Select(chapter => (chapter.Title, chapter.Text)).ShouldBe(previous.Chapters.Select(chapter => (chapter.Title, chapter.Text)));
        next.Chapters.Select(chapter => chapter.Id).Intersect(previous.Chapters.Select(chapter => chapter.Id)).ShouldBeEmpty();
        previous.Status.ShouldBe("Published");
    }

    [Fact]
    public async Task A_copied_question_stays_approved_until_its_chapter_changes()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("editor");
        var first = await PublishedAsync(client, cancellationToken);
        var id = await DraftNextAsync(client, first.Id, cancellationToken);
        var copied = await ReadContentAsync(client, id, cancellationToken);
        var chapter = copied.Chapters.ShouldHaveSingleItem();
        copied.Questions.Where(question => question.ChapterId == chapter.Id).ShouldAllBe(question => question.Review == "Approved");

        var path = new Uri($"/editorial/course-versions/{id}/chapters/{chapter.Id}", UriKind.Relative);
        using (var replaced = await client.PutAsJsonAsync(path, new { title = chapter.Title, text = "Neu: vor und nach …" }, cancellationToken))
        {
            replaced.StatusCode.ShouldBe(HttpStatusCode.OK, $"PUT {path} answered {(int)replaced.StatusCode}.");
        }

        var changed = await ReadContentAsync(client, id, cancellationToken);
        var questions = changed.Questions.Where(question => question.ChapterId == chapter.Id).ToList();
        questions.ShouldNotBeEmpty();
        questions.ShouldAllBe(question => question.Review == "Unreviewed");
    }

    [Fact]
    public async Task A_second_next_of_one_version_is_refused_while_the_first_exists()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("editor");
        var first = await PublishedAsync(client, cancellationToken);
        await DraftNextAsync(client, first.Id, cancellationToken);

        using var refused = await client.PostAsync(NextOf(first.Id), null, cancellationToken);

        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await CodeOfAsync(refused, cancellationToken)).ShouldBe("editorial.next-version-exists");
        (await CountListedAsync(client, first.CourseId, 2, cancellationToken)).ShouldBe(1);
    }

    [Theory]
    [InlineData("Draft")]
    [InlineData("Retired")]
    public async Task Next_of_a_version_that_is_not_published_is_refused(string state)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("editor");
        var id = state switch
        {
            "Draft" => await CreateAsync(client, cancellationToken),
            "Retired" => await RetiredAsync(client, cancellationToken),
            _ => throw new InvalidOperationException($"No version in state {state}."),
        };

        using var refused = await client.PostAsync(NextOf(id), null, cancellationToken);

        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict, $"POST {NextOf(id)} of a {state} version answered {(int)refused.StatusCode}.");
        (await CodeOfAsync(refused, cancellationToken)).ShouldBe("editorial.not-published");
    }

    [Fact]
    public async Task Next_of_an_unknown_version_is_not_found()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("editor");

        foreach (var id in new[] { Guid.CreateVersion7(), Guid.Empty })
        {
            using var response = await client.PostAsync(NextOf(id), null, cancellationToken);

            response.StatusCode.ShouldBe(HttpStatusCode.NotFound, $"POST {NextOf(id)} answered {(int)response.StatusCode}.");
            (await CodeOfAsync(response, cancellationToken)).ShouldBe("editorial.course-version-not-found");
        }
    }

    /// <summary>
    /// Another writer took number 2 for the course and has not committed: the handler's check does not see it, the
    /// INSERT waits on the unique index, and once the writer commits the index refuses it. Neither the unit of work nor
    /// the handler catches that; the host answers 409 <c>already-exists</c>.
    /// </summary>
    [Fact]
    public async Task A_next_version_that_loses_the_race_at_the_index_answers_already_exists_never_a_server_error()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("editor");
        var connectionString = await EditorialDatabase.ConnectionStringAsync(aspire, cancellationToken);
        var first = await PublishedAsync(client, cancellationToken);

        await using (var gate = await EditorialDatabase.HoldVersionNumberAsync(connectionString, first.CourseId, 2, cancellationToken))
        {
            var pending = client.PostAsync(NextOf(first.Id), null, cancellationToken);
            await EditorialDatabase.WaitUntilBlockedAsync(connectionString, gate, 1, cancellationToken);
            await gate.CommitAsync(cancellationToken);

            using var response = await pending;

            response.StatusCode.ShouldBe(HttpStatusCode.Conflict, await response.Content.ReadAsStringAsync(cancellationToken));
            (await CodeOfAsync(response, cancellationToken)).ShouldBe("already-exists");
        }

        (await EditorialDatabase.CountVersionsNumberedAsync(connectionString, first.CourseId, 2, cancellationToken)).ShouldBe(1L);
    }

    [Fact]
    public async Task The_copied_questions_keep_who_approved_them_and_when()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("editor");
        var first = await PublishedAsync(client, cancellationToken);

        var id = await DraftNextAsync(client, first.Id, cancellationToken);

        var previous = await ReadContentAsync(client, first.Id, cancellationToken);
        var next = await ReadContentAsync(client, id, cancellationToken);
        previous.Questions.ShouldNotBeEmpty();
        previous.Questions.ShouldAllBe(question => question.Review == "Approved" && question.ReviewedBy != null && question.ReviewedAt != null);
        next.Questions.Select(question => (question.Review, question.ReviewedBy, question.ReviewedAt))
            .ShouldBe(previous.Questions.Select(question => (question.Review, question.ReviewedBy, question.ReviewedAt)));
    }

    /// <summary>The approval is a historical record of the copied question: the reviewer's name travels with it (#205).</summary>
    [Fact]
    public async Task The_copied_questions_keep_the_reviewers_name_and_the_approval_time()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("editor");
        var first = await PublishedAsync(client, cancellationToken);

        var id = await DraftNextAsync(client, first.Id, cancellationToken);

        var previous = await ReadContentAsync(client, first.Id, cancellationToken);
        var next = await ReadContentAsync(client, id, cancellationToken);
        previous.Questions.ShouldNotBeEmpty();
        previous.Questions.ShouldAllBe(question => question.ApprovedByName == "Emma Editor" && question.ApprovedAt != null);
        next.Questions.Select(question => (question.ApprovedByName, question.ApprovedAt))
            .ShouldBe(previous.Questions.Select(question => (question.ApprovedByName, question.ApprovedAt)));
    }

    private static Uri NextOf(Guid id) => new($"/editorial/course-versions/{id}/next", UriKind.Relative);

    private static async Task<Guid> CreateAsync(HttpClient client, CancellationToken cancellationToken)
    {
        using var created = await client.PostAsJsonAsync(new Uri("/editorial/courses", UriKind.Relative), new { title = $"Hygiene {Guid.CreateVersion7()}" }, cancellationToken);
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        return await created.Content.ReadFromJsonAsync<Guid>(cancellationToken);
    }

    private static async Task<Guid> RetiredAsync(HttpClient client, CancellationToken cancellationToken)
    {
        var published = await PublishedAsync(client, cancellationToken);
        using var retired = await StepAsync(client, published.Id, "retire", cancellationToken);
        retired.StatusCode.ShouldBe(HttpStatusCode.OK, $"POST …/{published.Id}/retire answered {(int)retired.StatusCode}.");
        return published.Id;
    }

    private static async Task<VersionContent> ReadContentAsync(HttpClient client, Guid id, CancellationToken cancellationToken)
    {
        var version = await client.GetFromJsonAsync<VersionContent>(new Uri($"/editorial/course-versions/{id}", UriKind.Relative), cancellationToken);
        version.ShouldNotBeNull();
        return version;
    }

    /// <summary>How many of the listed versions belong to the course and carry the number, read a page at a time until the pages run out.</summary>
    private static async Task<int> CountListedAsync(HttpClient client, Guid courseId, int number, CancellationToken cancellationToken)
    {
        var count = 0;
        for (var page = 1; ; page++)
        {
            var listed = await client.GetFromJsonAsync<WorkflowSummaryPage>(new Uri($"/editorial/course-versions?page={page}&pageSize=100", UriKind.Relative), cancellationToken);
            listed.ShouldNotBeNull();
            count += listed.Items.Count(version => version.CourseId == courseId && version.Number == number);
            if (listed.Items.Count == 0 || page * listed.PageSize >= listed.TotalCount)
            {
                return count;
            }
        }
    }

    private sealed record VersionContent(Guid Id, Guid CourseId, int Number, string Status, IReadOnlyList<ChapterContent> Chapters, IReadOnlyList<QuestionContent> Questions);

    private sealed record ChapterContent(Guid Id, string Title, string? Text);

    private sealed record QuestionContent(Guid Id, Guid ChapterId, string Review, Guid? ReviewedBy, DateTimeOffset? ReviewedAt, string? ApprovedByName, DateTimeOffset? ApprovedAt);
}
