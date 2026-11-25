using System.Net;
using System.Text;
using System.Text.Json;

namespace SmartSolutionsLab.PharmaTraining.DemoSeeder.Tests;

/// <summary>The responses the fake answers with, shaped as the real services shape theirs.</summary>
public static class FakeResponses
{
    public static HttpResponseMessage Json(HttpStatusCode status, object? value) => new(status)
    {
        Content = new StringContent(JsonSerializer.Serialize(value, JsonSerializerOptions.Web), Encoding.UTF8, "application/json"),
    };

    public static HttpResponseMessage Ok(object? value) => Json(HttpStatusCode.OK, value);

    /// <summary>201 with the new id as a JSON string and its location, as the modules' creates answer.</summary>
    public static HttpResponseMessage Created(string id, string location)
    {
        var response = Json(HttpStatusCode.Created, id);
        response.Headers.Location = new Uri(location, UriKind.RelativeOrAbsolute);
        return response;
    }

    public static HttpResponseMessage NoContent() => new(HttpStatusCode.NoContent);

    /// <summary>Problem details carrying the error's code, as ToHttpResult answers a failure.</summary>
    public static HttpResponseMessage Problem(HttpStatusCode status, string code)
    {
        var body = JsonSerializer.Serialize(new { type = "about:blank", title = code, status = (int)status, code }, JsonSerializerOptions.Web);
        return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/problem+json") };
    }

    public static HttpResponseMessage Html(HttpStatusCode status, string html) => new(status)
    {
        Content = new StringContent(html, Encoding.UTF8, "text/html"),
    };

    /// <summary>A request the fake has no route for: the test names it, so a missing route is never taken for a refusal.</summary>
    public static HttpResponseMessage Unscripted(HttpRequestMessage request) => new(HttpStatusCode.NotImplemented)
    {
        Content = new StringContent($"The fake has no route for {request.Method} {request.RequestUri}", Encoding.UTF8, "text/plain"),
    };
}
