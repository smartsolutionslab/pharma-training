using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// An editor asks the AI for a content review of a course version through the edge (story E6): the route needs the
/// policy editorial.edit, so a pharmacy owner is forbidden and a caller without a token unauthorized. This run has no
/// model (ADR 0001), so an editor reaches the review and gets 503 model-not-configured; the review's 200 with hints is
/// proven with a stand-in model in ReviewContentTests. Whatever the review answers, the version stays as it was.
/// </summary>
[Collection(EditorialDrafts.Name)]
public class EditorialContentReviewTests(AspireFixture aspire)
{
    private const string ModelNotConfigured = "model-not-configured";

    private static readonly Uri Courses = new("/editorial/courses", UriKind.Relative);
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// What a holder of editorial.edit gets in this run: the run has no model, so the review answers 503 with the code
    /// model-not-configured before any handler runs (ADR 0001). The 200 path is ReviewContentTests', with a stand-in model.
    /// </summary>
    [Fact]
    public async Task An_editor_reaches_the_review_and_without_a_model_gets_503_model_not_configured()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("editor");
        var id = await CreateWithTextChapterAsync(client, cancellationToken);

        using var response = await client.PostAsync(Review(id), null, cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        (await CodeOfAsync(response, cancellationToken)).ShouldBe(ModelNotConfigured);
    }

    [Fact]
    public async Task A_review_leaves_the_version_unchanged()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("editor");
        var id = await CreateWithTextChapterAsync(client, cancellationToken);
        var before = await ReadAsync(client, id, cancellationToken);

        using (var review = await client.PostAsync(Review(id), null, cancellationToken))
        {
            review.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable, "the review was reached");
        }

        (await ReadAsync(client, id, cancellationToken)).ShouldBe(before);
    }

    [Fact]
    public async Task A_pharmacy_owner_is_forbidden()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Guid id;
        using (var editor = await aspire.CreateHostClientAsync("editor"))
        {
            id = await CreateWithTextChapterAsync(editor, cancellationToken);
        }

        using var client = await aspire.CreateHostClientAsync("owner");

        using var response = await client.PostAsync(Review(id), null, cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_anonymous_caller_is_unauthorized()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = aspire.CreateHostClient();

        using var response = await client.PostAsync(Review(Guid.CreateVersion7()), null, cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private static Uri Review(Guid id) => new($"/editorial/course-versions/{id}/content-review", UriKind.Relative);

    private static async Task<Guid> CreateWithTextChapterAsync(HttpClient client, CancellationToken cancellationToken)
    {
        Guid id;
        using (var created = await client.PostAsJsonAsync(Courses, new { title = $"Kühlkette {Guid.CreateVersion7()}" }, cancellationToken))
        {
            created.StatusCode.ShouldBe(HttpStatusCode.Created);
            id = await created.Content.ReadFromJsonAsync<Guid>(cancellationToken);
        }

        var chapter = new { title = "Lagerung", text = "Kühlpflichtige Arzneimittel lagern zwischen 2 und 8 °C." };
        using var added = await client.PostAsJsonAsync(new Uri($"/editorial/course-versions/{id}/chapters", UriKind.Relative), chapter, cancellationToken);
        added.StatusCode.ShouldBe(HttpStatusCode.Created);
        return id;
    }

    /// <summary>The version as the edge answers it, as raw JSON, so any change to it shows.</summary>
    private static async Task<string> ReadAsync(HttpClient client, Guid id, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(new Uri($"/editorial/course-versions/{id}", UriKind.Relative), cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private static async Task<string> CodeOfAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        body.ShouldNotBeNullOrWhiteSpace($"{response.RequestMessage?.Method} {response.RequestMessage?.RequestUri} answered {(int)response.StatusCode} without problem details.");
        var problem = JsonSerializer.Deserialize<Problem>(body, Web);
        problem.ShouldNotBeNull();
        return problem.Code;
    }

    private sealed record Problem(string Code);
}
