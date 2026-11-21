using System.Net;
using System.Net.Http.Json;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// Every course version route requires the policy editorial.edit, which the realm role editor holds: an editor reaches
/// each of them, a pharmacy owner is forbidden, and a caller without a token is unauthorized.
/// </summary>
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
    public async Task An_editor_reaches_the_route(string method, string route)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("editor");
        var id = await CreateAsync(client, cancellationToken);
        var chapter = await AddChapterAsync(client, id, cancellationToken);
        using var request = Request(method, route, id, chapter);

        using var response = await client.SendAsync(request, cancellationToken);

        response.IsSuccessStatusCode.ShouldBeTrue($"{method} {request.RequestUri} answered {(int)response.StatusCode}.");
    }

    [Theory]
    [InlineData("POST", "courses")]
    [InlineData("GET", "course-versions")]
    [InlineData("GET", "course-versions/{id}")]
    [InlineData("POST", "course-versions/{id}/chapters")]
    [InlineData("PUT", "course-versions/{id}/chapters/{chapterId}")]
    [InlineData("DELETE", "course-versions/{id}/chapters/{chapterId}")]
    [InlineData("PUT", "course-versions/{id}/chapters/order")]
    public async Task A_pharmacy_owner_is_forbidden(string method, string route)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("owner");
        using var request = Request(method, route, Guid.CreateVersion7(), Guid.CreateVersion7());

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
    public async Task An_anonymous_caller_is_unauthorized(string method, string route)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = aspire.CreateHostClient();
        using var request = Request(method, route, Guid.CreateVersion7(), Guid.CreateVersion7());

        using var response = await client.SendAsync(request, cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private static HttpRequestMessage Request(string method, string route, Guid id, Guid chapter)
    {
        var path = route.Replace("{id}", id.ToString(), StringComparison.Ordinal).Replace("{chapterId}", chapter.ToString(), StringComparison.Ordinal);
        return new HttpRequestMessage(new HttpMethod(method), new Uri($"/editorial/{path}", UriKind.Relative))
        {
            Content = (method, route) switch
            {
                ("POST", "courses") => JsonContent.Create(new { title = $"Hygiene {Guid.CreateVersion7()}" }),
                ("POST", "course-versions/{id}/chapters") or ("PUT", "course-versions/{id}/chapters/{chapterId}") =>
                    JsonContent.Create(new { title = "Händehygiene", text = "Vor und nach …" }),
                ("PUT", "course-versions/{id}/chapters/order") => JsonContent.Create(new { chapters = new[] { chapter } }),
                _ => null,
            },
        };
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
