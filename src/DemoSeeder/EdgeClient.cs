using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SmartSolutionsLab.PharmaTraining.DemoSeeder;

/// <summary>
/// Calls the edge under <c>/api</c> as a demo user, with the user's own bearer token, signing them in when they have none
/// or a call answers 401. A call is sent once: a refusal whose code means "not yet" (a copy another service has not
/// received) is polled on the <see cref="Poller"/>; any other failure stops the run, naming the call, the user and the code.
/// </summary>
internal sealed class EdgeClient(HttpClient http, Uri edgeUrl, Func<string, CancellationToken, Task<string>> signIn, Poller poller)
{
    private const int PageSize = 100;

    private readonly Dictionary<string, string> tokens = [with(StringComparer.Ordinal)];

    /// <summary>The edge's <c>/api/config.json</c>, read before anyone signs in.</summary>
    public static async Task<JsonNode> ConfigurationAsync(HttpClient http, Uri edgeUrl, CancellationToken cancellationToken)
    {
        var uri = Address(edgeUrl, "/config.json");
        using var response = await http.GetAsync(uri, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new SeedingException($"GET {uri.AbsolutePath} answered {(int)response.StatusCode}.");
        }

        return JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken)) ?? throw new SeedingException($"GET {uri.AbsolutePath} answered nothing.");
    }

    /// <summary>One call, whatever it answers.</summary>
    public async Task<EdgeAnswer> SendAsync(string user, HttpMethod method, string path, JsonNode? body, CancellationToken cancellationToken)
    {
        var answer = await SendOnceAsync(user, method, path, body, cancellationToken);
        if (answer.Status != HttpStatusCode.Unauthorized)
        {
            return answer;
        }

        // The token expired: a 401 was refused before anything ran, so the call is sent again with a new one.
        tokens.Remove(user);
        return await SendOnceAsync(user, method, path, body, cancellationToken);
    }

    /// <summary>A call that must succeed, polled while it is refused with one of the codes that mean "not yet".</summary>
    public async Task<EdgeAnswer> CallAsync(string user, HttpMethod method, string path, JsonNode? body, CancellationToken cancellationToken, params string[] eventualCodes) =>
        await poller.UntilAsync(Describe(user, method, path), async token =>
        {
            var answer = await SendAsync(user, method, path, body, token);
            if (answer.Succeeded)
            {
                return Polled<EdgeAnswer>.Done(answer);
            }

            return answer.Code is not null && eventualCodes.Contains(answer.Code, StringComparer.Ordinal)
                ? Polled<EdgeAnswer>.Wait($"answering {(int)answer.Status} {answer.Code}")
                : throw Failure(user, method, path, answer);
        }, cancellationToken);

    /// <summary>A create that answers the new record's id as a JSON string.</summary>
    public async Task<string> CreateAsync(string user, string path, JsonNode? body, CancellationToken cancellationToken)
    {
        var answer = await CallAsync(user, HttpMethod.Post, path, body, cancellationToken);
        return answer.Body?.GetValueKind() == JsonValueKind.String
            ? answer.Body.GetValue<string>()
            : throw new SeedingException($"{Describe(user, HttpMethod.Post, path)} answered {(int)answer.Status} without the new id.");
    }

    public async Task<JsonNode> GetAsync(string user, string path, CancellationToken cancellationToken) =>
        (await CallAsync(user, HttpMethod.Get, path, null, cancellationToken)).Body ?? new JsonObject();

    /// <summary>Every item of a paged list, page by page.</summary>
    public async Task<IReadOnlyList<JsonNode>> AllAsync(string user, string path, CancellationToken cancellationToken)
    {
        List<JsonNode> items = [];
        var separator = path.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        for (var page = 1; ; page++)
        {
            var result = await GetAsync(user, $"{path}{separator}page={page}&pageSize={PageSize}", cancellationToken);
            items.AddRange(Json.Items(result, "items"));
            if (page * PageSize >= Json.Int(result, "totalCount"))
            {
                return items;
            }
        }
    }

    public static SeedingException Failure(string user, HttpMethod method, string path, EdgeAnswer answer) =>
        new($"{Describe(user, method, path)} answered {(int)answer.Status}{(answer.Code is null ? string.Empty : " " + answer.Code)}.");

    private static string Describe(string user, HttpMethod method, string path) => $"{method} /api{path} as {user}";

    private static Uri Address(Uri edgeUrl, string path) => new($"{edgeUrl.ToString().TrimEnd('/')}/api{path}");

    private async Task<EdgeAnswer> SendOnceAsync(string user, HttpMethod method, string path, JsonNode? body, CancellationToken cancellationToken)
    {
        if (!tokens.TryGetValue(user, out var token))
        {
            token = await signIn(user, cancellationToken);
            tokens[user] = token;
        }

        using var request = new HttpRequestMessage(method, Address(edgeUrl, path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
        {
            request.Content = Json.Content(body);
        }

        using var response = await http.SendAsync(request, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        var json = Parse(text, response.Content.Headers.ContentType?.MediaType);
        return new EdgeAnswer(response.StatusCode, json, response.IsSuccessStatusCode ? null : Json.Text(json, "code"));
    }

    private static JsonNode? Parse(string text, string? mediaType)
    {
        if (string.IsNullOrWhiteSpace(text) || mediaType is null || !mediaType.Contains("json", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(text);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
