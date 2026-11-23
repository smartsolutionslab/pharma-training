using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Edge.Tests;

/// <summary>Whether an answer of the edge is the web app's <c>index.html</c> from the test's web root, and how it may be cached.</summary>
public static class AppAnswer
{
    public static async Task ShouldBeTheAppAsync(this HttpResponseMessage response, string request)
    {
        Ensure.That(response).IsNotNull();

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        response.Headers.Contains(ModuleService.MarkerHeader).ShouldBeFalse($"{request} reached the module's service ({(int)response.StatusCode}: {Shorten(body)}) instead of answering the web app's index.html.");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, $"{request} should answer the web app's index.html (200); it answered {(int)response.StatusCode}: {Shorten(body)}");
        response.Content.Headers.ContentType?.MediaType.ShouldBe("text/html", $"{request} should answer index.html as text/html.");
        body.ShouldContain(WebRoot.AppMarker, Case.Sensitive, $"{request} answered 200, but not with the web root's index.html: {Shorten(body)}");
        response.Headers.CacheControl?.NoCache.ShouldBe(true, $"{request} answered index.html with Cache-Control '{response.Headers.CacheControl}'; it must be no-cache, so a new deployment's app is loaded at once.");
    }

    public static async Task ShouldNotBeTheAppAsync(this HttpResponseMessage response, string request)
    {
        Ensure.That(response).IsNotNull();

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.ShouldNotContain(WebRoot.AppMarker, Case.Sensitive, $"{request} answered the web app's index.html ({(int)response.StatusCode}); it is not a page of the app.");
    }

    private static string Shorten(string body) => body.Length <= 200 ? body : body[..200] + "…";
}
