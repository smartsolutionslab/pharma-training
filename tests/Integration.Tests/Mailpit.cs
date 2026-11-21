using System.Net.Http.Json;
using Aspire.Hosting.Testing;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// The mail Mailpit caught, read through its HTTP API. Tests send to fresh addresses, so a search by recipient finds
/// only their own mail.
/// </summary>
internal sealed class Mailpit(AspireFixture aspire)
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Waits until Mailpit holds at least <paramref name="atLeast"/> mails to the address, polling once a second for at
    /// most 30 seconds, and answers all of them oldest first; fails the test naming the address when fewer arrive.
    /// </summary>
    public async Task<List<CaughtMail>> MessagesToAsync(string address, int atLeast, CancellationToken cancellationToken)
    {
        using var client = new HttpClient { BaseAddress = aspire.App.GetEndpoint("mailpit", "http") };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        var search = new Uri($"/api/v1/search?query={Uri.EscapeDataString($"to:{address}")}", UriKind.Relative);

        List<Summary> found = [];
        while (true)
        {
            var result = await client.GetFromJsonAsync<SearchResult>(search, cancellationToken);
            found = [.. result?.Messages ?? []];
            if (found.Count >= atLeast || timeout.IsCancellationRequested)
            {
                break;
            }

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }

        found.Count.ShouldBeGreaterThanOrEqualTo(atLeast, $"no {atLeast} mail(s) reached {address} within {Timeout.TotalSeconds:0} s");

        List<CaughtMail> mails = [];
        foreach (var summary in found.OrderBy(summary => summary.Created))
        {
            var message = await client.GetFromJsonAsync<Message>(new Uri($"/api/v1/message/{summary.Id}", UriKind.Relative), cancellationToken)
                ?? throw new InvalidOperationException($"Mailpit lost message {summary.Id}.");
            mails.Add(new CaughtMail(message.Subject, message.Text, message.From.Address, [.. message.To.Select(to => to.Address)], summary.Created));
        }

        return mails;
    }

    /// <summary>A mail as the tests read it.</summary>
    internal sealed record CaughtMail(string Subject, string Text, string From, IReadOnlyList<string> To, DateTimeOffset Created);

    private sealed record SearchResult(IReadOnlyList<Summary> Messages);

    private sealed record Summary([property: System.Text.Json.Serialization.JsonPropertyName("ID")] string Id, DateTimeOffset Created);

    private sealed record Message(string Subject, string Text, Mailbox From, IReadOnlyList<Mailbox> To);

    private sealed record Mailbox(string Address);
}
