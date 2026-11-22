using System.Net.Http.Json;
using Aspire.Hosting.Testing;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// The mail Mailpit caught, read through its HTTP API. Tests send to fresh addresses, so a search by recipient finds
/// only their own mail. A wait lasts 30 seconds unless the test gives it longer.
/// </summary>
internal sealed class Mailpit(AspireFixture aspire, TimeSpan? timeout = null)
{
    private readonly TimeSpan wait = timeout ?? TimeSpan.FromSeconds(30);

    /// <summary>
    /// Waits until Mailpit holds at least <paramref name="atLeast"/> mails to the address, polling once a second for at
    /// most the wait, and answers all of them oldest first; fails the test naming the address when fewer arrive.
    /// </summary>
    public async Task<List<CaughtMail>> MessagesToAsync(string address, int atLeast, CancellationToken cancellationToken)
    {
        using var client = new HttpClient { BaseAddress = aspire.App.GetEndpoint("mailpit", "http") };
        using var expired = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        expired.CancelAfter(wait);
        var search = new Uri($"/api/v1/search?query={Uri.EscapeDataString($"to:{address}")}", UriKind.Relative);

        List<Summary> found = [];
        while (true)
        {
            var result = await client.GetFromJsonAsync<SearchResult>(search, cancellationToken);
            found = [.. result?.Messages ?? []];
            if (found.Count >= atLeast || expired.IsCancellationRequested)
            {
                break;
            }

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }

        found.Count.ShouldBeGreaterThanOrEqualTo(atLeast, $"no {atLeast} mail(s) reached {address} within {wait.TotalSeconds:0} s");

        List<CaughtMail> mails = [];
        foreach (var summary in found.OrderBy(summary => summary.Created))
        {
            var message = await client.GetFromJsonAsync<Message>(new Uri($"/api/v1/message/{summary.Id}", UriKind.Relative), cancellationToken)
                ?? throw new InvalidOperationException($"Mailpit lost message {summary.Id}.");
            mails.Add(new CaughtMail(message.Subject, message.Text, message.From.Address, [.. message.To.Select(to => to.Address)], summary.Created));
        }

        return mails;
    }

    /// <summary>
    /// Waits until Mailpit holds a mail to the address whose subject contains <paramref name="subject"/>, polling once a
    /// second for at most the wait, and answers it; fails the test naming the address and subject when none arrives, or
    /// when more than one does. The realm users' addresses are shared by tests that run at once, so a test picks its own
    /// mail by something only it knows, such as its pharmacy's name.
    /// </summary>
    public async Task<CaughtMail> MessageToAsync(string address, string subject, CancellationToken cancellationToken)
    {
        using var client = new HttpClient { BaseAddress = aspire.App.GetEndpoint("mailpit", "http") };
        using var expired = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        expired.CancelAfter(wait);
        var search = new Uri($"/api/v1/search?query={Uri.EscapeDataString($"to:{address}")}", UriKind.Relative);

        List<Summary> found = [];
        while (true)
        {
            var result = await client.GetFromJsonAsync<SearchResult>(search, cancellationToken);
            found = [.. (result?.Messages ?? []).Where(summary => summary.Subject.Contains(subject, StringComparison.Ordinal))];
            if (found.Count > 0 || expired.IsCancellationRequested)
            {
                break;
            }

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }

        found.Count.ShouldBe(1, $"not exactly one mail to {address} about \"{subject}\" within {wait.TotalSeconds:0} s");
        var message = await client.GetFromJsonAsync<Message>(new Uri($"/api/v1/message/{found[0].Id}", UriKind.Relative), cancellationToken)
            ?? throw new InvalidOperationException($"Mailpit lost message {found[0].Id}.");
        return new CaughtMail(message.Subject, message.Text, message.From.Address, [.. message.To.Select(to => to.Address)], found[0].Created);
    }

    /// <summary>The number of mails Mailpit holds to the address now, without waiting for any.</summary>
    public async Task<int> CountToAsync(string address, CancellationToken cancellationToken)
    {
        using var client = new HttpClient { BaseAddress = aspire.App.GetEndpoint("mailpit", "http") };
        var search = new Uri($"/api/v1/search?query={Uri.EscapeDataString($"to:{address}")}", UriKind.Relative);
        var result = await client.GetFromJsonAsync<SearchResult>(search, cancellationToken);
        return result?.Messages.Count ?? 0;
    }

    /// <summary>A mail as the tests read it.</summary>
    internal sealed record CaughtMail(string Subject, string Text, string From, IReadOnlyList<string> To, DateTimeOffset Created);

    private sealed record SearchResult(IReadOnlyList<Summary> Messages);

    private sealed record Summary([property: System.Text.Json.Serialization.JsonPropertyName("ID")] string Id, DateTimeOffset Created, string Subject);

    private sealed record Message(string Subject, string Text, Mailbox From, IReadOnlyList<Mailbox> To);

    private sealed record Mailbox(string Address);
}
