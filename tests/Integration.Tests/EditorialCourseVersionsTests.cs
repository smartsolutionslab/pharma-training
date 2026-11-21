using System.Net;
using System.Net.Http.Json;
using Aspire.Hosting.Testing;
using Npgsql;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// An editor creates a course and edits the Markdown chapters of its first version through the edge: every change is
/// read back in order, the inbox lists versions by status, and a published version refuses every chapter change.
/// </summary>
public class EditorialCourseVersionsTests(AspireFixture aspire)
{
    private static readonly Uri Courses = new("/editorial/courses", UriKind.Relative);

    [Fact]
    public async Task A_created_course_is_read_back_as_its_first_draft_without_chapters()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("editor");
        var title = $"Hygiene {Guid.CreateVersion7()}";

        using var created = await client.PostAsJsonAsync(Courses, new { title }, cancellationToken);

        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var id = await created.Content.ReadFromJsonAsync<Guid>(cancellationToken);
        created.Headers.Location.ShouldBe(new Uri($"/editorial/course-versions/{id}", UriKind.Relative));
        var version = await ReadAsync(client, id, cancellationToken);
        version.Id.ShouldBe(id);
        version.CourseId.ShouldNotBe(Guid.Empty);
        version.Number.ShouldBe(1);
        version.Title.ShouldBe(title);
        version.Status.ShouldBe("Draft");
        version.Chapters.ShouldBeEmpty();
    }

    [Fact]
    public async Task Chapters_are_added_replaced_reordered_and_removed_and_read_back_in_their_order()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("editor");
        var id = await CreateAsync(client, $"Hygiene {Guid.CreateVersion7()}", cancellationToken);

        using var addedA = await client.PostAsJsonAsync(Chapters(id), new { title = "Händehygiene", text = "# Hände\n\nVor und nach …" }, cancellationToken);
        addedA.StatusCode.ShouldBe(HttpStatusCode.Created);
        addedA.Headers.Location.ShouldBe(new Uri($"/editorial/course-versions/{id}", UriKind.Relative));
        var a = await addedA.Content.ReadFromJsonAsync<Guid>(cancellationToken);
        var b = await AddChapterAsync(client, id, "Flächendesinfektion", "Wischen, nicht sprühen.", cancellationToken);
        (await ReadAsync(client, id, cancellationToken)).Chapters.ShouldBe(
        [
            new ChapterView(a, 1, "Händehygiene", "Text", "# Hände\n\nVor und nach …"),
            new ChapterView(b, 2, "Flächendesinfektion", "Text", "Wischen, nicht sprühen."),
        ]);

        using var replaced = await client.PutAsJsonAsync(Chapter(id, a), new { title = "Händehygiene, überarbeitet", text = "**Neu:** vor und nach …" }, cancellationToken);
        replaced.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ReadAsync(client, id, cancellationToken)).Chapters.ShouldBe(
        [
            new ChapterView(a, 1, "Händehygiene, überarbeitet", "Text", "**Neu:** vor und nach …"),
            new ChapterView(b, 2, "Flächendesinfektion", "Text", "Wischen, nicht sprühen."),
        ]);

        using var reordered = await client.PutAsJsonAsync(Order(id), new { chapters = new[] { b, a } }, cancellationToken);
        reordered.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ReadAsync(client, id, cancellationToken)).Chapters.ShouldBe(
        [
            new ChapterView(b, 1, "Flächendesinfektion", "Text", "Wischen, nicht sprühen."),
            new ChapterView(a, 2, "Händehygiene, überarbeitet", "Text", "**Neu:** vor und nach …"),
        ]);

        using var removed = await client.DeleteAsync(Chapter(id, a), cancellationToken);
        removed.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ReadAsync(client, id, cancellationToken)).Chapters.ShouldBe(
        [
            new ChapterView(b, 1, "Flächendesinfektion", "Text", "Wischen, nicht sprühen."),
        ]);
    }

    [Fact]
    public async Task A_draft_is_listed_among_the_drafts_a_page_at_a_time_and_not_among_the_published()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("editor");
        var title = $"Hygiene {Guid.CreateVersion7()}";
        var id = await CreateAsync(client, title, cancellationToken);

        var drafts = await ListAsync(client, "status=Draft&page=1&pageSize=100", cancellationToken);
        var published = await ListAsync(client, "status=Published&page=1&pageSize=100", cancellationToken);
        var onePerPage = await ListAsync(client, "status=Draft&page=1&pageSize=1", cancellationToken);

        var listed = drafts.Items.Where(version => version.Id == id).ShouldHaveSingleItem();
        listed.ShouldBe(new Summary(id, listed.CourseId, 1, title, "Draft"));
        drafts.PageSize.ShouldBe(100);
        drafts.Items.ShouldAllBe(version => version.Status == "Draft");
        published.Items.ShouldNotContain(version => version.Id == id);
        published.Items.ShouldAllBe(version => version.Status == "Published");
        onePerPage.Items.Count.ShouldBe(1);
        onePerPage.PageSize.ShouldBe(1);
        onePerPage.TotalCount.ShouldBeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task A_published_version_refuses_every_chapter_change_and_stays_as_it_was()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("editor");
        var title = $"Hygiene {Guid.CreateVersion7()}";
        var id = await CreateAsync(client, title, cancellationToken);
        var chapter = await AddChapterAsync(client, id, "Händehygiene", "Vor und nach …", cancellationToken);
        await SeedPublishedAsync(id, cancellationToken);
        var before = await ReadAsync(client, id, cancellationToken);
        before.Status.ShouldBe("Published");

        using var added = await client.PostAsJsonAsync(Chapters(id), new { title = "Neu", text = "Neu." }, cancellationToken);
        using var replaced = await client.PutAsJsonAsync(Chapter(id, chapter), new { title = "Neu", text = "Neu." }, cancellationToken);
        using var reordered = await client.PutAsJsonAsync(Order(id), new { chapters = new[] { chapter } }, cancellationToken);
        using var removed = await client.DeleteAsync(Chapter(id, chapter), cancellationToken);

        foreach (var refused in new[] { added, replaced, reordered, removed })
        {
            refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
            (await CodeOfAsync(refused, cancellationToken)).ShouldBe("editorial.not-a-draft");
        }

        var after = await ReadAsync(client, id, cancellationToken);
        after.Title.ShouldBe(before.Title);
        after.Status.ShouldBe("Published");
        after.Chapters.ShouldBe([new ChapterView(chapter, 1, "Händehygiene", "Text", "Vor und nach …")]);
        var published = await ListAsync(client, "status=Published&page=1&pageSize=100", cancellationToken);
        published.Items.ShouldContain(new Summary(id, before.CourseId, 1, title, "Published"));
    }

    [Fact]
    public async Task A_blank_course_title_is_refused_as_the_callers_mistake()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("editor");

        using var refused = await client.PostAsJsonAsync(Courses, new { title = "  " }, cancellationToken);

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await CodeOfAsync(refused, cancellationToken)).ShouldBe("editorial.invalid-course-title");
    }

    [Fact]
    public async Task A_chapter_without_a_title_or_text_or_an_order_without_chapters_is_refused_as_the_callers_mistake()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("editor");
        var id = await CreateAsync(client, $"Hygiene {Guid.CreateVersion7()}", cancellationToken);

        using var untitled = await client.PostAsJsonAsync(Chapters(id), new { title = " ", text = "Vor und nach …" }, cancellationToken);
        using var empty = await client.PostAsJsonAsync(Chapters(id), new { title = "Händehygiene", text = " " }, cancellationToken);
        using var unordered = await client.PutAsJsonAsync(Order(id), new { }, cancellationToken);

        untitled.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await CodeOfAsync(untitled, cancellationToken)).ShouldBe("editorial.invalid-chapter-title");
        empty.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await CodeOfAsync(empty, cancellationToken)).ShouldBe("editorial.invalid-chapter-text");
        unordered.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await CodeOfAsync(unordered, cancellationToken)).ShouldBe("editorial.invalid-chapter-order");
        (await ReadAsync(client, id, cancellationToken)).Chapters.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_unknown_version_or_chapter_is_not_found()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("editor");
        var id = await CreateAsync(client, $"Hygiene {Guid.CreateVersion7()}", cancellationToken);

        using var unknownVersion = await client.GetAsync(Version(Guid.CreateVersion7()), cancellationToken);
        using var emptyVersion = await client.GetAsync(Version(Guid.Empty), cancellationToken);
        using var unknownChapter = await client.PutAsJsonAsync(Chapter(id, Guid.CreateVersion7()), new { title = "Neu", text = "Neu." }, cancellationToken);
        using var removedStranger = await client.DeleteAsync(Chapter(id, Guid.CreateVersion7()), cancellationToken);

        unknownVersion.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CodeOfAsync(unknownVersion, cancellationToken)).ShouldBe("editorial.course-version-not-found");
        emptyVersion.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CodeOfAsync(emptyVersion, cancellationToken)).ShouldBe("editorial.course-version-not-found");
        unknownChapter.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CodeOfAsync(unknownChapter, cancellationToken)).ShouldBe("editorial.chapter-not-found");
        removedStranger.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CodeOfAsync(removedStranger, cancellationToken)).ShouldBe("editorial.chapter-not-found");
    }

    [Fact]
    public async Task An_empty_version_or_chapter_id_on_a_chapter_route_is_not_found()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("editor");
        var id = await CreateAsync(client, $"Hygiene {Guid.CreateVersion7()}", cancellationToken);
        var chapter = await AddChapterAsync(client, id, "Händehygiene", "Vor und nach …", cancellationToken);

        using var addedToNone = await client.PostAsJsonAsync(Chapters(Guid.Empty), new { title = "Neu", text = "Neu." }, cancellationToken);
        using var replacedInNone = await client.PutAsJsonAsync(Chapter(Guid.Empty, chapter), new { title = "Neu", text = "Neu." }, cancellationToken);
        using var removedFromNone = await client.DeleteAsync(Chapter(Guid.Empty, chapter), cancellationToken);
        using var orderedInNone = await client.PutAsJsonAsync(Order(Guid.Empty), new { chapters = new[] { chapter } }, cancellationToken);
        using var replacedNone = await client.PutAsJsonAsync(Chapter(id, Guid.Empty), new { title = "Neu", text = "Neu." }, cancellationToken);
        using var removedNone = await client.DeleteAsync(Chapter(id, Guid.Empty), cancellationToken);

        foreach (var response in new[] { addedToNone, replacedInNone, removedFromNone, orderedInNone })
        {
            response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
            (await CodeOfAsync(response, cancellationToken)).ShouldBe("editorial.course-version-not-found");
        }

        foreach (var response in new[] { replacedNone, removedNone })
        {
            response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
            (await CodeOfAsync(response, cancellationToken)).ShouldBe("editorial.chapter-not-found");
        }

        (await ReadAsync(client, id, cancellationToken)).Chapters.Select(read => read.Id).ShouldBe([chapter]);
    }

    [Fact]
    public async Task An_order_that_misses_a_chapter_is_a_conflict()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("editor");
        var id = await CreateAsync(client, $"Hygiene {Guid.CreateVersion7()}", cancellationToken);
        var a = await AddChapterAsync(client, id, "Händehygiene", "Vor und nach …", cancellationToken);
        var b = await AddChapterAsync(client, id, "Flächendesinfektion", "Wischen.", cancellationToken);

        using var refused = await client.PutAsJsonAsync(Order(id), new { chapters = new[] { b } }, cancellationToken);

        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await CodeOfAsync(refused, cancellationToken)).ShouldBe("editorial.order-does-not-match");
        (await ReadAsync(client, id, cancellationToken)).Chapters.Select(chapter => chapter.Id).ShouldBe([a, b]);
    }

    [Fact]
    public async Task A_status_that_is_none_is_refused_as_the_callers_mistake()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("editor");

        using var refused = await client.GetAsync(new Uri("/editorial/course-versions?status=7", UriKind.Relative), cancellationToken);

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await CodeOfAsync(refused, cancellationToken)).ShouldBe("editorial.invalid-status");
    }

    [Theory]
    [InlineData("InReview,Published")]
    [InlineData("Draft,Published")]
    public async Task A_list_of_statuses_is_refused_rather_than_read_as_another_status(string statuses)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("editor");

        using var refused = await client.GetAsync(new Uri($"/editorial/course-versions?status={statuses}", UriKind.Relative), cancellationToken);

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await CodeOfAsync(refused, cancellationToken)).ShouldBe("editorial.invalid-status");
    }

    [Fact]
    public async Task Of_two_concurrent_removals_of_one_chapter_one_succeeds_and_the_other_is_told_the_version_changed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("editor");
        var id = await CreateAsync(client, $"Hygiene {Guid.CreateVersion7()}", cancellationToken);
        var chapter = await AddChapterAsync(client, id, "Händehygiene", "Vor und nach …", cancellationToken);
        var connectionString = await EditorialConnectionStringAsync(cancellationToken);

        HttpResponseMessage[] responses;
        await using (var gate = await LockVersionAsync(connectionString, id, cancellationToken))
        {
            var first = client.DeleteAsync(Chapter(id, chapter), cancellationToken);
            var second = client.DeleteAsync(Chapter(id, chapter), cancellationToken);
            await WaitUntilBlockedAsync(connectionString, 2, cancellationToken);
            await gate.CommitAsync(cancellationToken);
            responses = await Task.WhenAll(first, second);
        }

        try
        {
            responses.Select(response => response.StatusCode).ShouldBe([HttpStatusCode.OK, HttpStatusCode.Conflict], ignoreOrder: true);
            var lost = responses.Single(response => response.StatusCode == HttpStatusCode.Conflict);
            (await CodeOfAsync(lost, cancellationToken)).ShouldBe("editorial.course-version-changed");
            (await ReadAsync(client, id, cancellationToken)).Chapters.ShouldBeEmpty();
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    [Fact]
    public async Task Of_two_concurrent_additions_to_one_version_one_is_created_and_the_other_is_told_the_version_changed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("editor");
        var id = await CreateAsync(client, $"Hygiene {Guid.CreateVersion7()}", cancellationToken);
        var existing = await AddChapterAsync(client, id, "Händehygiene", "Vor und nach …", cancellationToken);
        var connectionString = await EditorialConnectionStringAsync(cancellationToken);

        HttpResponseMessage[] responses;
        await using (var gate = await LockVersionAsync(connectionString, id, cancellationToken))
        {
            var first = client.PostAsJsonAsync(Chapters(id), new { title = "Flächendesinfektion", text = "Wischen." }, cancellationToken);
            var second = client.PostAsJsonAsync(Chapters(id), new { title = "Instrumente", text = "Aufbereiten." }, cancellationToken);
            await WaitUntilBlockedAsync(connectionString, 2, cancellationToken);
            await gate.CommitAsync(cancellationToken);
            responses = await Task.WhenAll(first, second);
        }

        try
        {
            responses.Select(response => response.StatusCode).ShouldBe([HttpStatusCode.Created, HttpStatusCode.Conflict], ignoreOrder: true);
            var lost = responses.Single(response => response.StatusCode == HttpStatusCode.Conflict);
            (await CodeOfAsync(lost, cancellationToken)).ShouldBe("editorial.course-version-changed");
            var won = responses.Single(response => response.StatusCode == HttpStatusCode.Created);
            var added = await won.Content.ReadFromJsonAsync<Guid>(cancellationToken);
            var chapters = (await ReadAsync(client, id, cancellationToken)).Chapters;
            chapters.Select(chapter => chapter.Id).ShouldBe([existing, added]);
            chapters.Select(chapter => chapter.Position).ShouldBe([1, 2]);
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    [Fact]
    public async Task Of_a_removal_and_a_replacement_of_one_chapter_at_once_one_succeeds_and_the_other_is_told_the_version_changed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("editor");
        var id = await CreateAsync(client, $"Hygiene {Guid.CreateVersion7()}", cancellationToken);
        var chapter = await AddChapterAsync(client, id, "Händehygiene", "Vor und nach …", cancellationToken);
        var connectionString = await EditorialConnectionStringAsync(cancellationToken);

        HttpResponseMessage[] responses;
        await using (var gate = await LockVersionAsync(connectionString, id, cancellationToken))
        {
            var removed = client.DeleteAsync(Chapter(id, chapter), cancellationToken);
            var replaced = client.PutAsJsonAsync(Chapter(id, chapter), new { title = "Händehygiene, überarbeitet", text = "Neu." }, cancellationToken);
            await WaitUntilBlockedAsync(connectionString, 2, cancellationToken);
            await gate.CommitAsync(cancellationToken);
            responses = await Task.WhenAll(removed, replaced);
        }

        try
        {
            await ShouldBeOneWonAndOneChangedAsync(responses, cancellationToken);
            var chapters = (await ReadAsync(client, id, cancellationToken)).Chapters;
            if (responses[0].StatusCode == HttpStatusCode.OK)
            {
                chapters.ShouldBeEmpty("the removal won, so the replacement must not have been saved");
            }
            else
            {
                chapters.ShouldBe([new ChapterView(chapter, 1, "Händehygiene, überarbeitet", "Text", "Neu.")], "the replacement won, so the removal must not have been saved");
            }
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    [Fact]
    public async Task Of_a_removal_and_an_order_at_once_one_succeeds_and_the_other_is_told_the_version_changed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("editor");
        var id = await CreateAsync(client, $"Hygiene {Guid.CreateVersion7()}", cancellationToken);
        var a = await AddChapterAsync(client, id, "Händehygiene", "Vor und nach …", cancellationToken);
        var b = await AddChapterAsync(client, id, "Flächendesinfektion", "Wischen.", cancellationToken);
        var c = await AddChapterAsync(client, id, "Instrumente", "Aufbereiten.", cancellationToken);
        var connectionString = await EditorialConnectionStringAsync(cancellationToken);

        // The removal writes only c's row and the order only a's and b's: no chapter row tells them apart, only the
        // version's row version does.
        HttpResponseMessage[] responses;
        await using (var gate = await LockVersionAsync(connectionString, id, cancellationToken))
        {
            var removed = client.DeleteAsync(Chapter(id, c), cancellationToken);
            var reordered = client.PutAsJsonAsync(Order(id), new { chapters = new[] { b, a, c } }, cancellationToken);
            await WaitUntilBlockedAsync(connectionString, 2, cancellationToken);
            await gate.CommitAsync(cancellationToken);
            responses = await Task.WhenAll(removed, reordered);
        }

        try
        {
            await ShouldBeOneWonAndOneChangedAsync(responses, cancellationToken);
            var chapters = (await ReadAsync(client, id, cancellationToken)).Chapters.Select(chapter => chapter.Id);
            if (responses[0].StatusCode == HttpStatusCode.OK)
            {
                chapters.ShouldBe([a, b], "the removal won, so the order must not have been saved");
            }
            else
            {
                chapters.ShouldBe([b, a, c], "the order won, so the removal must not have been saved");
            }
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    [Fact]
    public async Task A_removal_and_an_order_that_write_the_chapters_in_opposite_orders_never_fail_the_server()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("editor");
        var connectionString = await EditorialConnectionStringAsync(cancellationToken);

        // Removing c3 of [c3, c1, c2] deletes its row, then moves c1 and c2 up; the order [c2, c3, c1] writes c1, c2
        // and c3. Each save locks its chapters' rows before the version's, so the two take c1 and c3 in opposite
        // orders and PostgreSQL often ends one of them as a deadlock (40P01). Five rounds make sure some do.
        for (var round = 0; round < 5; round++)
        {
            var id = await CreateAsync(client, $"Hygiene {Guid.CreateVersion7()}", cancellationToken);
            var c1 = await AddChapterAsync(client, id, "Händehygiene", "Vor und nach …", cancellationToken);
            var c2 = await AddChapterAsync(client, id, "Flächendesinfektion", "Wischen.", cancellationToken);
            var c3 = await AddChapterAsync(client, id, "Instrumente", "Aufbereiten.", cancellationToken);
            using (var first = await client.PutAsJsonAsync(Order(id), new { chapters = new[] { c3, c1, c2 } }, cancellationToken))
            {
                first.StatusCode.ShouldBe(HttpStatusCode.OK);
            }

            HttpResponseMessage[] responses;
            await using (var gate = await LockVersionAsync(connectionString, id, cancellationToken))
            {
                var removed = client.DeleteAsync(Chapter(id, c3), cancellationToken);
                var reordered = client.PutAsJsonAsync(Order(id), new { chapters = new[] { c2, c3, c1 } }, cancellationToken);
                await WaitUntilBlockedAsync(connectionString, 2, cancellationToken);
                await gate.CommitAsync(cancellationToken);
                responses = await Task.WhenAll(removed, reordered);
            }

            try
            {
                await ShouldBeOneWonAndOneChangedAsync(responses, cancellationToken);
            }
            finally
            {
                foreach (var response in responses)
                {
                    response.Dispose();
                }
            }
        }
    }

    [Fact]
    public async Task A_page_size_above_the_maximum_is_refused()
    {
        using var client = await aspire.CreateHostClientAsync("editor");

        using var refused = await client.GetAsync(new Uri("/editorial/course-versions?pageSize=101", UriKind.Relative), TestContext.Current.CancellationToken);

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    private static Uri Version(Guid id) => new($"/editorial/course-versions/{id}", UriKind.Relative);

    private static Uri Chapters(Guid id) => new($"/editorial/course-versions/{id}/chapters", UriKind.Relative);

    private static Uri Chapter(Guid id, Guid chapter) => new($"/editorial/course-versions/{id}/chapters/{chapter}", UriKind.Relative);

    private static Uri Order(Guid id) => new($"/editorial/course-versions/{id}/chapters/order", UriKind.Relative);

    private static async Task<Guid> CreateAsync(HttpClient client, string title, CancellationToken cancellationToken)
    {
        using var created = await client.PostAsJsonAsync(Courses, new { title }, cancellationToken);
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        return await created.Content.ReadFromJsonAsync<Guid>(cancellationToken);
    }

    private static async Task<Guid> AddChapterAsync(HttpClient client, Guid id, string title, string text, CancellationToken cancellationToken)
    {
        using var added = await client.PostAsJsonAsync(Chapters(id), new { title, text }, cancellationToken);
        added.StatusCode.ShouldBe(HttpStatusCode.Created);
        return await added.Content.ReadFromJsonAsync<Guid>(cancellationToken);
    }

    private static async Task<CourseVersion> ReadAsync(HttpClient client, Guid id, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(Version(id), cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var version = await response.Content.ReadFromJsonAsync<CourseVersion>(cancellationToken);
        version.ShouldNotBeNull();
        return version;
    }

    private static async Task<SummaryPage> ListAsync(HttpClient client, string query, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(new Uri($"/editorial/course-versions?{query}", UriKind.Relative), cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var page = await response.Content.ReadFromJsonAsync<SummaryPage>(cancellationToken);
        page.ShouldNotBeNull();
        return page;
    }

    private static async Task<string> CodeOfAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var problem = await response.Content.ReadFromJsonAsync<Problem>(cancellationToken);
        problem.ShouldNotBeNull();
        return problem.Code;
    }

    private static async Task ShouldBeOneWonAndOneChangedAsync(HttpResponseMessage[] responses, CancellationToken cancellationToken)
    {
        List<string> answers = [];
        foreach (var response in responses)
        {
            answers.Add($"{(int)response.StatusCode} {await response.Content.ReadAsStringAsync(cancellationToken)}");
        }

        responses.Select(response => response.StatusCode).ShouldBe([HttpStatusCode.OK, HttpStatusCode.Conflict], ignoreOrder: true, string.Join(" | ", answers));
        var lost = responses.Single(response => response.StatusCode == HttpStatusCode.Conflict);
        (await CodeOfAsync(lost, cancellationToken)).ShouldBe("editorial.course-version-changed");
    }

    private async Task<string> EditorialConnectionStringAsync(CancellationToken cancellationToken) =>
        await aspire.App.GetConnectionStringAsync("editorial-database", cancellationToken)
            ?? throw new InvalidOperationException("The editorial database has no connection string.");

    /// <summary>
    /// Opens a transaction that holds the version's row and its chapters' rows, so every request that writes them
    /// waits after it has loaded the version until the transaction ends. Disposing the gate closes its connection.
    /// </summary>
    private static async Task<VersionLock> LockVersionAsync(string connectionString, Guid id, CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            var transaction = await connection.BeginTransactionAsync(cancellationToken);
            await using (var version = new NpgsqlCommand("SELECT 1 FROM editorial.course_versions WHERE id = @id FOR UPDATE", connection, transaction))
            {
                version.Parameters.AddWithValue("id", id);
                (await version.ExecuteScalarAsync(cancellationToken)).ShouldNotBeNull();
            }

            await using (var chapters = new NpgsqlCommand("SELECT 1 FROM editorial.course_version_chapters WHERE course_version_id = @id FOR UPDATE", connection, transaction))
            {
                chapters.Parameters.AddWithValue("id", id);
                await chapters.ExecuteNonQueryAsync(cancellationToken);
            }

            return new VersionLock(connection, transaction);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    /// <summary>
    /// Polls the editorial database until the given number of statements on course versions wait for a lock, and
    /// fails the test when they do not within 30 seconds.
    /// </summary>
    private static async Task WaitUntilBlockedAsync(string connectionString, int expected, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT count(*) FROM pg_stat_activity
            WHERE wait_event_type = 'Lock' AND datname = current_database() AND pid <> pg_backend_pid()
              AND query ILIKE '%course_version%'
            """,
            connection);

        var blocked = 0L;
        while (blocked < expected)
        {
            if (timeout.IsCancellationRequested)
            {
                blocked.ShouldBe(expected, "the concurrent requests never waited for the lock on the version");
            }

            blocked = (long)(await command.ExecuteScalarAsync(cancellationToken) ?? 0L);
            if (blocked < expected)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);
            }
        }

        blocked.ShouldBe(expected);
    }

    /// <summary>
    /// Publishing through the API needs submitting and publishing (#18), which need approved questions (#16) and a
    /// blueprint (#17). Until then the test publishes the version in its own database; #18 replaces this seed with
    /// the real submit and publish calls.
    /// </summary>
    private async Task SeedPublishedAsync(Guid id, CancellationToken cancellationToken)
    {
        var connectionString = await EditorialConnectionStringAsync(cancellationToken);
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("UPDATE editorial.course_versions SET status = 'Published' WHERE id = @id", connection);
        command.Parameters.AddWithValue("id", id);

        (await command.ExecuteNonQueryAsync(cancellationToken)).ShouldBe(1);
    }

    private sealed record CourseVersion(Guid Id, Guid CourseId, int Number, string Title, string Status, IReadOnlyList<ChapterView> Chapters);

    private sealed record ChapterView(Guid Id, int Position, string Title, string Kind, string? Text);

    private sealed record Summary(Guid Id, Guid CourseId, int Number, string Title, string Status);

    private sealed record SummaryPage(IReadOnlyList<Summary> Items, int TotalCount, int Page, int PageSize);

    private sealed record Problem(string Code);
}
