using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace SmartSolutionsLab.PharmaTraining.DemoSeeder;

/// <summary>
/// Reads invitation tokens from Mailpit: the newest mail to the address whose subject names the pharmacy, waited for on
/// the <see cref="Poller"/>; the token is the link's fragment (docs/adr/0014). Staging's Mailpit is read with its basic
/// auth (<c>user:password</c>); the local one has none.
/// </summary>
internal sealed class Mailbox(HttpClient http, Uri mailpitUrl, string? basicAuth, Poller poller)
{
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(1);

    public Task<string> TokenAsync(string address, string pharmacy, CancellationToken cancellationToken) =>
        poller.UntilAsync($"The invitation to {address} for {pharmacy}", async token =>
        {
            var found = await GetAsync($"/api/v1/search?query={Uri.EscapeDataString($"to:{address}")}", token);
            var newest = Json.Items(found, "messages")
                .Where(message => Json.Text(message, "Subject")?.Contains(pharmacy, StringComparison.Ordinal) == true)
                .MaxBy(message => message["Created"]?.GetValue<DateTimeOffset>());
            if (newest is null)
            {
                return Polled<string>.Wait("without a mail in Mailpit");
            }

            var mail = await GetAsync($"/api/v1/message/{Uri.EscapeDataString(Json.Required(newest, "ID", "Mailpit's search"))}", token);
            var link = Regex.Match(Json.Text(mail, "Text") ?? string.Empty, "#token=([A-Za-z0-9_-]{43})", RegexOptions.None, MatchTimeout);
            return link.Success ? Polled<string>.Done(link.Groups[1].Value) : throw new SeedingException($"The invitation to {address} carries no token.");
        }, cancellationToken);

    private async Task<JsonNode?> GetAsync(string path, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri($"{mailpitUrl.ToString().TrimEnd('/')}{path}"));
        if (basicAuth is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(basicAuth)));
        }

        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new SeedingException($"Mailpit's {path.Split('?')[0]} answered {(int)response.StatusCode}.");
        }

        return JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
    }
}
