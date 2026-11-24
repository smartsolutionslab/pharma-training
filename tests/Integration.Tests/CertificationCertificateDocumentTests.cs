using System.Net;
using System.Net.Http.Json;
using System.Text;
using static SmartSolutionsLab.PharmaTraining.Integration.Tests.EditorialWorkflow;
using static SmartSolutionsLab.PharmaTraining.Integration.Tests.LearningSessions;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// The holder reads their certificate's PDF through the edge (#50): <c>GET /certification/certificates/{id}/pdf</c>
/// answers a five-minute read URL on Certification's own blob, which the browser reads straight from storage, inline as
/// <c>application/pdf</c>. Until the document is stored it answers 409; to anyone but the holder, and for an id nobody
/// holds, 404, so nobody learns which certificates exist.
/// </summary>
public class CertificationCertificateDocumentTests(AspireFixture aspire)
{
    private const string StartedType = "SmartSolutionsLab.PharmaTraining.Contracts.Organisation.MembershipStartedV1";
    private const string PublishedType = "SmartSolutionsLab.PharmaTraining.Contracts.Editorial.CourseVersionPublishedV1";
    private const string PassedType = "SmartSolutionsLab.PharmaTraining.Contracts.Assessment.TestPassedV1";
    private const string CertificateOfAttempt = "select id from certification.certificates where attempt_id = @id";

    // Rendering waits on Wolverine's local queue, and a first render loads the fonts; a minute is plenty.
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    /// <summary>Reads the URL Certification signed straight from the storage emulator, as the holder's browser does.</summary>
    private static readonly HttpClient Storage = new();

    [Fact]
    public async Task Without_a_token_it_answers_401()
    {
        using var client = aspire.CreateHostClient();

        using var response = await client.GetAsync(Pdf(Guid.CreateVersion7()), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task An_unknown_certificate_answers_404()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("developer");

        using var response = await client.GetAsync(Pdf(Guid.CreateVersion7()), cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound, await response.Content.ReadAsStringAsync(cancellationToken));
        (await CodeOfAsync(response, cancellationToken)).ShouldBe("certification.certificate-not-found");
    }

    /// <summary>
    /// End to end: the developer passes a test, Certification issues the certificate and renders its PDF, and the
    /// developer's URL serves the PDF inline from Certification's container.
    /// </summary>
    [Fact]
    public async Task The_holder_gets_a_url_that_serves_the_certificates_pdf_inline()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var holder = await aspire.CreateHostClientAsync("developer");
        var certificateId = await IssueAsync(SubjectOf(holder), cancellationToken);

        // The PDF is rendered by a message of its own after issuing; until it is stored the endpoint answers 409.
        DocumentBody answer;
        using (var document = await WaitUntilAsync(() => holder.GetAsync(Pdf(certificateId), cancellationToken), HttpStatusCode.OK, $"GET {Pdf(certificateId)}", cancellationToken))
        {
            answer = (await document.Content.ReadFromJsonAsync<DocumentBody>(cancellationToken)).ShouldNotBeNull();
        }

        answer.ExpiresAt.ShouldBeGreaterThan(TimeProvider.System.GetUtcNow());
        answer.ExpiresAt.ShouldBeLessThanOrEqualTo(TimeProvider.System.GetUtcNow().AddMinutes(5));
        answer.Url.AbsolutePath.ShouldEndWith($"/certification/certificates/{certificateId:D}.pdf");
        using var read = await Storage.GetAsync(answer.Url, cancellationToken);
        read.StatusCode.ShouldBe(HttpStatusCode.OK, await read.Content.ReadAsStringAsync(cancellationToken));
        read.Content.Headers.ContentType.ShouldNotBeNull().MediaType.ShouldBe("application/pdf");
        read.Content.Headers.ContentDisposition.ShouldNotBeNull().DispositionType.ShouldBe("inline");
        var bytes = await read.Content.ReadAsByteArrayAsync(cancellationToken);
        Encoding.ASCII.GetString(bytes, 0, 5).ShouldBe("%PDF-");
    }

    /// <summary>Someone signed in who does not hold the certificate learns nothing about it: 404, as for an unknown id.</summary>
    [Fact]
    public async Task Someone_else_gets_404_for_the_holders_stored_certificate()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var holder = await aspire.CreateHostClientAsync("developer");
        using var visitor = await aspire.CreateHostClientAsync("visitor");
        var certificateId = await IssueAsync(SubjectOf(holder), cancellationToken);
        using (var stored = await WaitUntilAsync(() => holder.GetAsync(Pdf(certificateId), cancellationToken), HttpStatusCode.OK, $"GET {Pdf(certificateId)}", cancellationToken))
        {
            stored.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        using var response = await visitor.GetAsync(Pdf(certificateId), cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound, await response.Content.ReadAsStringAsync(cancellationToken));
        (await CodeOfAsync(response, cancellationToken)).ShouldBe("certification.certificate-not-found");
    }

    private static Uri Pdf(Guid certificateId) => new($"/certification/certificates/{certificateId:D}/pdf", UriKind.Relative);

    private static EventMetadata NewMetadata() => new(Guid.CreateVersion7(), TimeProvider.System.GetUtcNow());

    /// <summary>The person starts a membership, passes a published version's test, and Certification issues the certificate; answers its id.</summary>
    private async Task<Guid> IssueAsync(Guid personId, CancellationToken cancellationToken)
    {
        var now = TimeProvider.System.GetUtcNow();
        var started = new StartedEvent(Guid.CreateVersion7(), personId, Guid.CreateVersion7(), "Dana Developer", "Pta", "Employee", now, NewMetadata());
        var published = new PublishedEvent(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            2,
            $"Hygiene {Guid.CreateVersion7()}",
            12,
            [new SentChapter(Guid.CreateVersion7(), 1, "Grundlagen", "Text", null, "# Grundlagen", null)],
            NewMetadata());
        var passed = new PassedEvent(Guid.CreateVersion7(), Guid.CreateVersion7(), personId, published.CourseId, published.CourseVersionId, 90, now, NewMetadata());
        await CertificationDatabase.DeliverAsync(aspire, StartedType, started, cancellationToken);
        await CertificationDatabase.DeliverAsync(aspire, PublishedType, published, cancellationToken);
        await CertificationDatabase.DeliverAsync(aspire, PassedType, passed, cancellationToken);

        await using var connection = await CertificationDatabase.OpenAsync(aspire, cancellationToken);
        await using var command = new Npgsql.NpgsqlCommand(CertificateOfAttempt, connection);
        command.Parameters.AddWithValue("id", passed.AttemptId);
        return (Guid)((await command.ExecuteScalarAsync(cancellationToken)) ?? throw new InvalidOperationException($"No certificate was issued for attempt {passed.AttemptId}."));
    }

    /// <summary>Sends the request until it answers the wanted status, for at most a minute; the response is the caller's to dispose.</summary>
    private static async Task<HttpResponseMessage> WaitUntilAsync(Func<Task<HttpResponseMessage>> send, HttpStatusCode wanted, string request, CancellationToken cancellationToken)
    {
        var started = TimeProvider.System.GetTimestamp();
        while (true)
        {
            var response = await send();
            if (response.StatusCode == wanted)
            {
                return response;
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var status = response.StatusCode;
            response.Dispose();
            if (TimeProvider.System.GetElapsedTime(started) >= WaitTimeout)
            {
                throw new TimeoutException($"{request} still answered {(int)status} {status} ({body}) after {WaitTimeout.TotalSeconds} s, not {(int)wanted}.");
            }

            await Task.Delay(PollInterval, cancellationToken);
        }
    }

    private sealed record DocumentBody(Uri Url, DateTimeOffset ExpiresAt);

    private sealed record EventMetadata(Guid EventIdentifier, DateTimeOffset OccurredAt);

    private sealed record StartedEvent(Guid MembershipId, Guid PersonId, Guid PharmacyId, string DisplayName, string JobFunction, string Role, DateTimeOffset StartedAt, EventMetadata Metadata);

    private sealed record SentChapter(Guid ChapterId, int Position, string Title, string Kind, int? VideoDurationSeconds, string? Text, string? File);

    private sealed record PublishedEvent(Guid CourseId, Guid CourseVersionId, int VersionNumber, string Title, int ValidityMonths, IReadOnlyList<SentChapter> Chapters, EventMetadata Metadata);

    private sealed record PassedEvent(Guid AttemptId, Guid SessionId, Guid PersonId, Guid CourseId, Guid CourseVersionId, int ScorePercent, DateTimeOffset PassedAt, EventMetadata Metadata);
}
