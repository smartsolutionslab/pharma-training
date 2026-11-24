using System.Net.Http.Headers;

namespace SmartSolutionsLab.PharmaTraining.Edge.Tests;

/// <summary>The requests a browser sends: a page load (a navigation), a call for data, a plain fetch of a file.</summary>
public static class Requests
{
    /// <summary>What a browser sends when it loads a page: <c>Sec-Fetch-Mode: navigate</c> and an Accept that lists HTML.</summary>
    public static HttpRequestMessage Navigation(string path, HttpMethod? method = null)
    {
        var request = new HttpRequestMessage(method ?? HttpMethod.Get, new Uri(path, UriKind.Relative));
        request.Headers.Add("Sec-Fetch-Mode", "navigate");
        request.Headers.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
        return request;
    }

    /// <summary>A page load from a client that sends no Fetch Metadata: only its Accept lists HTML.</summary>
    public static HttpRequestMessage HtmlAccepted(string path)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));
        request.Headers.Accept.ParseAdd("text/html,application/xhtml+xml,*/*;q=0.8");
        return request;
    }

    /// <summary>What the generated client sends: <c>Accept: application/json</c>, with a bearer token when one is given.</summary>
    public static HttpRequestMessage Data(string path, string? token = null, HttpMethod? method = null)
    {
        var request = new HttpRequestMessage(method ?? HttpMethod.Get, new Uri(path, UriKind.Relative));
        request.Headers.Add("Sec-Fetch-Mode", "cors");
        request.Headers.Accept.ParseAdd("application/json");
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return request;
    }

    /// <summary>A script or stylesheet the page loads: <c>Accept: */*</c>, no navigation.</summary>
    public static HttpRequestMessage File(string path)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));
        request.Headers.Add("Sec-Fetch-Mode", "no-cors");
        request.Headers.Accept.ParseAdd("*/*");
        return request;
    }
}
