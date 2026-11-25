using System.Net;
using Microsoft.Extensions.Time.Testing;

namespace SmartSolutionsLab.PharmaTraining.DemoSeeder.Tests;

/// <summary>
/// Mailpit's HTTP API as the seeder reads it: a search by recipient (<c>to:</c>, optionally <c>subject:</c>), newest
/// first, and a message by its ID with its text.
/// </summary>
public sealed class FakeMailpit(FakeTimeProvider time)
{
    private readonly List<CaughtMail> mails = [];

    public IReadOnlyList<CaughtMail> Mails => mails;

    public void Deliver(string to, string subject, string text) =>
        // A tick apart, so the newest mail is newest even when the clock stands still.
        mails.Add(new CaughtMail(Guid.CreateVersion7().ToString("N")[..22], to, subject, text, time.GetUtcNow().AddTicks(mails.Count)));

    internal FakeAnswer Handle(HttpRequestMessage request)
    {
        var path = request.RequestUri?.AbsolutePath ?? string.Empty;
        if (request.Method == HttpMethod.Get && path == "/api/v1/search")
        {
            var found = Search(FakeRoute.Pairs(request.RequestUri?.Query ?? string.Empty).GetValueOrDefault("query") ?? string.Empty);
            var summaries = found.OrderByDescending(mail => mail.Created).Select(mail => new
            {
                ID = mail.Id,
                Created = mail.Created,
                Subject = mail.Subject,
                Snippet = mail.Text.Length > 40 ? mail.Text[..40] : mail.Text,
                To = new[] { new { Name = string.Empty, Address = mail.To } },
            }).ToList();
            return new FakeAnswer("/api/v1/search", null, Json(new { total = mails.Count, messages_count = summaries.Count, messages = summaries }));
        }

        if (request.Method == HttpMethod.Get && FakeRoute.TryMatch("/api/v1/message/{id}", path, out var values))
        {
            var mail = mails.SingleOrDefault(each => each.Id == values["id"]);
            return new FakeAnswer("/api/v1/message/{id}", null, mail is null
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : Json(new { ID = mail.Id, Created = mail.Created, Subject = mail.Subject, Text = mail.Text, To = new[] { new { Name = string.Empty, Address = mail.To } } }));
        }

        return new FakeAnswer(string.Empty, null, FakeResponses.Unscripted(request));
    }

    // Mailpit names its fields in PascalCase (ID, Created, Subject, To, Address), unlike the services.
    private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(value), System.Text.Encoding.UTF8, "application/json"),
    };

    private List<CaughtMail> Search(string query)
    {
        IEnumerable<CaughtMail> found = mails;
        foreach (var term in query.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (term.StartsWith("to:", StringComparison.Ordinal))
            {
                var address = term[3..].Trim('"');
                found = found.Where(mail => string.Equals(mail.To, address, StringComparison.OrdinalIgnoreCase));
            }
            else if (term.StartsWith("subject:", StringComparison.Ordinal))
            {
                var subject = term[8..].Trim('"');
                found = found.Where(mail => mail.Subject.Contains(subject, StringComparison.OrdinalIgnoreCase));
            }
        }

        return [.. found];
    }

    /// <summary>A mail Mailpit caught.</summary>
    public sealed record CaughtMail(string Id, string To, string Subject, string Text, DateTimeOffset Created);
}
