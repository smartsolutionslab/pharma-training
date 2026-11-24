using System.Net;
using System.Net.Http.Json;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// Every course version route requires the policy editorial.edit, which the realm role editor holds: an editor reaches
/// each of them, a pharmacy owner is forbidden, and a caller without a token is unauthorized.
/// </summary>
[Collection(EditorialDrafts.Name)]
public class EditorialCourseVersionRoleTests(AspireFixture aspire)
{
    [Theory]
    [InlineData("POST", "courses")]
    [InlineData("GET", "course-versions")]
    [InlineData("GET", "course-versions/{id}")]
    [InlineData("POST", "course-versions/{id}/chapters")]
    [InlineData("PUT", "course-versions/{id}/chapters/{chapterId}")]
    [InlineData("DELETE", "course-versions/{id}/chapters/{chapterId}")]
    [InlineData("PUT", "course-versions/{id}/chapters/order")]
    [InlineData("POST", "course-versions/{id}/questions")]
    [InlineData("PUT", "course-versions/{id}/questions/{questionId}")]
    [InlineData("DELETE", "course-versions/{id}/questions/{questionId}")]
    [InlineData("POST", "course-versions/{id}/questions/{questionId}/approve")]
    [InlineData("POST", "course-versions/{id}/uploads")]
    [InlineData("PUT", "course-versions/{id}/blueprint")]
    [InlineData("GET", "course-versions/{id}/returns")]
    public async Task An_editor_reaches_the_route(string method, string route)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("editor");
        var id = await CreateAsync(client, cancellationToken);
        var chapter = await AddChapterAsync(client, id, cancellationToken);
        var question = route.Contains("{questionId}", StringComparison.Ordinal) ? await AddQuestionAsync(client, id, chapter, cancellationToken) : Guid.CreateVersion7();
        using var request = Request(method, route, id, chapter, question);

        using var response = await client.SendAsync(request, cancellationToken);

        response.IsSuccessStatusCode.ShouldBeTrue($"{method} {request.RequestUri} answered {(int)response.StatusCode}.");
    }

    /// <summary>
    /// The workflow's routes cannot succeed on a fresh empty draft, so an editor reaching one gets the domain's answer:
    /// the policy let them through and the handler ran.
    /// </summary>
    [Theory]
    [InlineData("submit", "editorial.no-chapters")]
    [InlineData("return", "editorial.not-in-review")]
    [InlineData("publish", "editorial.not-in-review")]
    [InlineData("retire", "editorial.not-published")]
    [InlineData("next", "editorial.not-published")]
    public async Task An_editor_reaches_the_workflow_route(string step, string code)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("editor");
        var id = await CreateAsync(client, cancellationToken);

        using var response = await EditorialWorkflow.StepAsync(client, id, step, cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict, $"POST /editorial/course-versions/{id}/{step} answered {(int)response.StatusCode}.");
        (await EditorialWorkflow.CodeOfAsync(response, cancellationToken)).ShouldBe(code);
    }

    [Theory]
    [InlineData("POST", "courses")]
    [InlineData("GET", "course-versions")]
    [InlineData("GET", "course-versions/{id}")]
    [InlineData("POST", "course-versions/{id}/chapters")]
    [InlineData("PUT", "course-versions/{id}/chapters/{chapterId}")]
    [InlineData("DELETE", "course-versions/{id}/chapters/{chapterId}")]
    [InlineData("PUT", "course-versions/{id}/chapters/order")]
    [InlineData("POST", "course-versions/{id}/questions")]
    [InlineData("PUT", "course-versions/{id}/questions/{questionId}")]
    [InlineData("DELETE", "course-versions/{id}/questions/{questionId}")]
    [InlineData("POST", "course-versions/{id}/questions/{questionId}/approve")]
    [InlineData("POST", "course-versions/{id}/uploads")]
    [InlineData("PUT", "course-versions/{id}/blueprint")]
    [InlineData("POST", "course-versions/{id}/submit")]
    [InlineData("POST", "course-versions/{id}/return")]
    [InlineData("POST", "course-versions/{id}/publish")]
    [InlineData("POST", "course-versions/{id}/retire")]
    [InlineData("GET", "course-versions/{id}/returns")]
    [InlineData("POST", "course-versions/{id}/next")]
    [InlineData("POST", "course-versions/republish")]
    public async Task A_pharmacy_owner_is_forbidden(string method, string route)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("owner");
        using var request = Request(method, route, Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());

        using var response = await client.SendAsync(request, cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("POST", "courses")]
    [InlineData("GET", "course-versions")]
    [InlineData("GET", "course-versions/{id}")]
    [InlineData("POST", "course-versions/{id}/chapters")]
    [InlineData("PUT", "course-versions/{id}/chapters/{chapterId}")]
    [InlineData("DELETE", "course-versions/{id}/chapters/{chapterId}")]
    [InlineData("PUT", "course-versions/{id}/chapters/order")]
    [InlineData("POST", "course-versions/{id}/questions")]
    [InlineData("PUT", "course-versions/{id}/questions/{questionId}")]
    [InlineData("DELETE", "course-versions/{id}/questions/{questionId}")]
    [InlineData("POST", "course-versions/{id}/questions/{questionId}/approve")]
    [InlineData("POST", "course-versions/{id}/uploads")]
    [InlineData("PUT", "course-versions/{id}/blueprint")]
    [InlineData("POST", "course-versions/{id}/submit")]
    [InlineData("POST", "course-versions/{id}/return")]
    [InlineData("POST", "course-versions/{id}/publish")]
    [InlineData("POST", "course-versions/{id}/retire")]
    [InlineData("GET", "course-versions/{id}/returns")]
    [InlineData("POST", "course-versions/{id}/next")]
    [InlineData("POST", "course-versions/republish")]
    public async Task An_anonymous_caller_is_unauthorized(string method, string route)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = aspire.CreateHostClient();
        using var request = Request(method, route, Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());

        using var response = await client.SendAsync(request, cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private static HttpRequestMessage Request(string method, string route, Guid id, Guid chapter, Guid question)
    {
        var path = route
            .Replace("{id}", id.ToString(), StringComparison.Ordinal)
            .Replace("{chapterId}", chapter.ToString(), StringComparison.Ordinal)
            .Replace("{questionId}", question.ToString(), StringComparison.Ordinal);
        return new HttpRequestMessage(new HttpMethod(method), new Uri($"/editorial/{path}", UriKind.Relative))
        {
            Content = (method, route) switch
            {
                ("POST", "courses") => JsonContent.Create(new { title = $"Hygiene {Guid.CreateVersion7()}" }),
                ("POST", "course-versions/{id}/chapters") or ("PUT", "course-versions/{id}/chapters/{chapterId}") =>
                    JsonContent.Create(new { title = "Händehygiene", text = "Vor und nach …" }),
                ("PUT", "course-versions/{id}/chapters/order") => JsonContent.Create(new { chapters = new[] { chapter } }),
                ("POST", "course-versions/{id}/uploads") => JsonContent.Create(new { kind = "Pdf" }),
                ("POST", "course-versions/{id}/questions") or ("PUT", "course-versions/{id}/questions/{questionId}") => JsonContent.Create(QuestionBody(chapter)),
                ("PUT", "course-versions/{id}/blueprint") => JsonContent.Create(new { questions = 10, passingScorePercent = 80, validityMonths = 12 }),
                _ => null,
            },
        };
    }

    private static object QuestionBody(Guid chapter) =>
        new
        {
            chapterId = chapter,
            text = "Wann desinfizieren Sie die Hände?",
            type = "SingleChoice",
            options = new[] { new { text = "Vor der Rezeptur", correct = true }, new { text = "Einmal am Tag", correct = false } },
        };

    private static async Task<Guid> AddQuestionAsync(HttpClient client, Guid id, Guid chapter, CancellationToken cancellationToken)
    {
        using var added = await client.PostAsJsonAsync(new Uri($"/editorial/course-versions/{id}/questions", UriKind.Relative), QuestionBody(chapter), cancellationToken);
        added.StatusCode.ShouldBe(HttpStatusCode.Created, $"POST /editorial/course-versions/{id}/questions answered {(int)added.StatusCode}.");
        return await added.Content.ReadFromJsonAsync<Guid>(cancellationToken);
    }

    private static async Task<Guid> CreateAsync(HttpClient client, CancellationToken cancellationToken)
    {
        using var created = await client.PostAsJsonAsync(new Uri("/editorial/courses", UriKind.Relative), new { title = $"Hygiene {Guid.CreateVersion7()}" }, cancellationToken);
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        return await created.Content.ReadFromJsonAsync<Guid>(cancellationToken);
    }

    private static async Task<Guid> AddChapterAsync(HttpClient client, Guid id, CancellationToken cancellationToken)
    {
        using var added = await client.PostAsJsonAsync(
            new Uri($"/editorial/course-versions/{id}/chapters", UriKind.Relative),
            new { title = "Händehygiene", text = "Vor und nach …" },
            cancellationToken);
        added.StatusCode.ShouldBe(HttpStatusCode.Created);
        return await added.Content.ReadFromJsonAsync<Guid>(cancellationToken);
    }
}
