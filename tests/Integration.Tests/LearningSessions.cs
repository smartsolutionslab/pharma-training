using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>Learning's session endpoints through the edge, as the learner's client calls them, and the session as they answer it.</summary>
internal static class LearningSessions
{
    public static readonly Uri Sessions = new("/learning/sessions", UriKind.Relative);

    public static Task<HttpResponseMessage> StartAsync(HttpClient client, Guid courseVersionId, CancellationToken cancellationToken)
    {
        Ensure.That(client).IsNotNull();
        return client.PostAsJsonAsync(Sessions, new { courseVersionId }, cancellationToken);
    }

    public static Task<HttpResponseMessage> ReadSessionAsync(HttpClient client, Guid id, CancellationToken cancellationToken)
    {
        Ensure.That(client).IsNotNull();
        return client.GetAsync(new Uri($"/learning/sessions/{id}", UriKind.Relative), cancellationToken);
    }

    /// <summary>Reports how far the learner got in one chapter, as the player's client does: <c>POST /learning/sessions/{id}/progress</c>.</summary>
    public static Task<HttpResponseMessage> ReportProgressAsync(HttpClient client, Guid sessionId, Guid chapterId, int percent, CancellationToken cancellationToken)
    {
        Ensure.That(client).IsNotNull();
        return client.PostAsJsonAsync(new Uri($"/learning/sessions/{sessionId}/progress", UriKind.Relative), new { chapterId, percent }, cancellationToken);
    }

    public static async Task<SessionBody> BodyOfAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        Ensure.That(response).IsNotNull();

        var body = await response.Content.ReadFromJsonAsync<SessionBody>(cancellationToken);
        body.ShouldNotBeNull();
        return body;
    }

    /// <summary>Two answers describe the same session in the same state, chapter by chapter.</summary>
    public static void ShouldDescribeTheSame(SessionBody actual, SessionBody expected)
    {
        Ensure.That(actual).IsNotNull();
        Ensure.That(expected).IsNotNull();

        (actual with { Chapters = [] }).ShouldBe(expected with { Chapters = [] });
        actual.Chapters.ShouldBe(expected.Chapters);
    }

    /// <summary>The person the client's access token names: its subject, read from the token's payload.</summary>
    public static Guid SubjectOf(HttpClient client)
    {
        Ensure.That(client).IsNotNull();

        var token = client.DefaultRequestHeaders.Authorization?.Parameter ?? throw new InvalidOperationException("The client sends no token.");
        var payload = token.Split('.')[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight(payload.Length + ((4 - (payload.Length % 4)) % 4), '=');
        using var document = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload)));
        return Guid.Parse(document.RootElement.GetProperty("sub").GetString() ?? string.Empty);
    }

    public sealed record SessionBody(Guid Id, Guid CourseId, Guid CourseVersionId, string Status, IReadOnlyList<ChapterBody> Chapters);

    public sealed record ChapterBody(Guid ChapterId, int Position, string Kind, int ProgressPercent, bool Completed);
}
