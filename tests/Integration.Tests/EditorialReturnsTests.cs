using System.Net;
using System.Net.Http.Json;
using static SmartSolutionsLab.PharmaTraining.Integration.Tests.EditorialWorkflow;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// Every return of a course version is kept with who returned it, when and why, and listed through the edge a page at
/// a time, newest first, the reason as the editor wrote it, markup included (#181).
/// </summary>
[Collection(EditorialDrafts.Name)]
public class EditorialReturnsTests(AspireFixture aspire)
{
    private const string FirstReason = "Erste Rückgabe: Quelle fehlt.";
    private const string SecondReason = "Zweite Rückgabe: <b>Kapitel 2</b> unklar.";

    [Fact]
    public async Task Two_returns_of_one_version_are_both_listed_newest_first()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("editor");
        var id = await ReturnedTwiceAsync(client, cancellationToken);

        var listed = await ReturnsAsync(client, id, string.Empty, cancellationToken);

        listed.TotalCount.ShouldBe(2);
        listed.Page.ShouldBe(1);
        listed.Items.Count.ShouldBe(2);
        listed.Items[0].Reason.ShouldBe(SecondReason);
        listed.Items[1].Reason.ShouldBe(FirstReason);
        listed.Items[0].ReturnedAt.ShouldBeGreaterThanOrEqualTo(listed.Items[1].ReturnedAt);
        listed.Items[0].ReturnedBy.ShouldNotBe(Guid.Empty);
        listed.Items[1].ReturnedBy.ShouldBe(listed.Items[0].ReturnedBy);
        listed.Items[0].Id.ShouldNotBe(listed.Items[1].Id);
    }

    [Fact]
    public async Task A_page_of_one_return_shows_the_older_return_on_page_two()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("editor");
        var id = await ReturnedTwiceAsync(client, cancellationToken);

        var listed = await ReturnsAsync(client, id, "?page=2&pageSize=1", cancellationToken);

        listed.Items.ShouldHaveSingleItem().Reason.ShouldBe(FirstReason);
        listed.TotalCount.ShouldBe(2);
    }

    [Fact]
    public async Task A_version_never_returned_lists_no_returns()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("editor");
        var draft = await ReadyToSubmitAsync(client, cancellationToken);

        var listed = await ReturnsAsync(client, draft.Id, string.Empty, cancellationToken);

        listed.Items.ShouldBeEmpty();
        listed.TotalCount.ShouldBe(0);
    }

    [Fact]
    public async Task The_returns_of_an_unknown_version_are_not_found()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("editor");

        foreach (var id in new[] { Guid.CreateVersion7(), Guid.Empty })
        {
            using var response = await client.GetAsync(ReturnsPath(id, string.Empty), cancellationToken);

            response.StatusCode.ShouldBe(HttpStatusCode.NotFound, $"GET {ReturnsPath(id, string.Empty)}");
            (await CodeOfAsync(response, cancellationToken)).ShouldBe("editorial.course-version-not-found", $"GET {ReturnsPath(id, string.Empty)}");
        }
    }

    private static Uri ReturnsPath(Guid id, string query) => new($"/editorial/course-versions/{id}/returns{query}", UriKind.Relative);

    /// <summary>A new course's first version, submitted and returned, submitted and returned again, by the calling editor.</summary>
    private static async Task<Guid> ReturnedTwiceAsync(HttpClient client, CancellationToken cancellationToken)
    {
        var draft = await ReadyToSubmitAsync(client, cancellationToken);
        await SubmitAsync(client, draft.Id, cancellationToken);
        await ReturnAsync(client, draft.Id, FirstReason, cancellationToken);
        await SubmitAsync(client, draft.Id, cancellationToken);
        await ReturnAsync(client, draft.Id, SecondReason, cancellationToken);
        return draft.Id;
    }

    private static async Task<ReturnsPage> ReturnsAsync(HttpClient client, Guid id, string query, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(ReturnsPath(id, query), cancellationToken);
        response.StatusCode.ShouldBe(
            HttpStatusCode.OK,
            $"GET {ReturnsPath(id, query)} answered {(int)response.StatusCode} {await response.Content.ReadAsStringAsync(cancellationToken)}");
        var page = await response.Content.ReadFromJsonAsync<ReturnsPage>(cancellationToken);
        page.ShouldNotBeNull();
        return page;
    }

    private sealed record ReturnsPage(List<ReturnItem> Items, int TotalCount, int Page, int PageSize);

    private sealed record ReturnItem(Guid Id, Guid ReturnedBy, DateTimeOffset ReturnedAt, string Reason);
}
