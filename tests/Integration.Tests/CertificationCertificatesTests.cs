using System.Text;
using System.Text.Json;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.Configuration;
using Npgsql;
using RabbitMQ.Client.Exceptions;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Storage;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// Issuing through the running application: Organisation's memberships and Editorial's course versions reach
/// Certification's local copies, and Assessment's passed test becomes exactly one certificate for its attempt, however
/// often it arrives, announced once as <c>CertificateIssuedV1</c>. Its PDF is rendered once, from a message that stays on
/// Certification's host, and stored at <c>certificates/{id}.pdf</c> in Certification's own container (#50). Its tables
/// are read directly.
/// </summary>
public class CertificationCertificatesTests(AspireFixture aspire)
{
    private const string StartedType = "SmartSolutionsLab.PharmaTraining.Contracts.Organisation.MembershipStartedV1";
    private const string EndedType = "SmartSolutionsLab.PharmaTraining.Contracts.Organisation.MembershipEndedV1";
    private const string PublishedType = "SmartSolutionsLab.PharmaTraining.Contracts.Editorial.CourseVersionPublishedV1";
    private const string PassedType = "SmartSolutionsLab.PharmaTraining.Contracts.Assessment.TestPassedV1";
    private const string CertificateIssuedExchange = "SmartSolutionsLab.PharmaTraining.Contracts.Certification.CertificateIssuedV1";
    private const string CertificatesByAttempt = "select count(*) from certification.certificates where attempt_id = @id";
    private const string StoredByAttempt = "select count(*) from certification.certificates where attempt_id = @id and document_state = 'Stored'";
    private const string DocumentRequestedExchange = "SmartSolutionsLab.PharmaTraining.Contracts.Certification.CertificateDocumentRequestedV1";

    // 23:30 UTC on 1 March 2027 is already 2 March in Germany; a yearly course is valid through 2 March 2028.
    private static readonly DateTimeOffset PassedAt = new(2027, 3, 1, 23, 30, 0, TimeSpan.Zero);
    private static readonly DateOnly LastValidDay = new(2028, 3, 2);

    [Fact]
    public async Task A_passed_test_issues_one_certificate_with_the_holders_name_the_course_title_its_version_number_and_its_last_valid_day()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var started = Started(Guid.CreateVersion7(), TimeProvider.System.GetUtcNow());
        var published = Published();
        var passed = Passed(started, published);

        await DeliverAsync(StartedType, started, cancellationToken);
        await DeliverAsync(PublishedType, published, cancellationToken);
        await DeliverAsync(PassedType, passed, cancellationToken);

        var certificate = (await CertificatesAsync(passed.AttemptId, cancellationToken)).ShouldHaveSingleItem();
        certificate.HolderId.ShouldBe(started.PersonId);
        certificate.HolderName.ShouldBe(started.DisplayName);
        certificate.CourseId.ShouldBe(published.CourseId);
        certificate.CourseVersionId.ShouldBe(published.CourseVersionId);
        certificate.CourseTitle.ShouldBe(published.Title);
        certificate.ScorePercent.ShouldBe(passed.ScorePercent);
        certificate.IssuedAt.ShouldBe(PassedAt);
        certificate.ValidUntil.ShouldBe(LastValidDay);
        certificate.CourseVersionNumber.ShouldBe(published.VersionNumber);
        certificate.VerificationCode.Length.ShouldBe(32);
        (await CountAsync("select count(*) from certification.course_versions where id = @id and version_number = 3", published.CourseVersionId, cancellationToken))
            .ShouldBe(1, "Certification's copy of the course version keeps its number");
    }

    /// <summary>
    /// Issuing requests the PDF in the same commit, so the certificate's document is soon stored: a PDF at
    /// <c>certificates/{id}.pdf</c> in Certification's own container. Its state was asserted Pending here before #50; it
    /// now moves on its own, so the test waits for Stored.
    /// </summary>
    [Fact]
    public async Task A_passed_test_stores_the_certificates_pdf_in_certifications_container()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var started = Started(Guid.CreateVersion7(), TimeProvider.System.GetUtcNow());
        var published = Published();
        var passed = Passed(started, published);
        await DeliverAsync(StartedType, started, cancellationToken);
        await DeliverAsync(PublishedType, published, cancellationToken);
        await DeliverAsync(PassedType, passed, cancellationToken);

        await CertificationDatabase.WaitUntilCountAsync(aspire, StoredByAttempt, passed.AttemptId, 1, cancellationToken);

        var certificate = (await CertificatesAsync(passed.AttemptId, cancellationToken)).ShouldHaveSingleItem();
        certificate.DocumentState.ShouldBe("Stored");
        certificate.DocumentLocation.ShouldBe($"certificates/{certificate.Id:D}.pdf");
        var blobs = await CertificationBlobsAsync(cancellationToken);
        var stored = await blobs.FindAsync(BlobName.From(certificate.DocumentLocation.ShouldNotBeNull()), cancellationToken);
        stored.HasValue.ShouldBeTrue("the PDF lies in Certification's own container");
        stored.Value.ContentType.ShouldBe("application/pdf");
        var start = await blobs.ReadStartAsync(stored.Value, 5, cancellationToken);
        Encoding.ASCII.GetString(start.Span).ShouldBe("%PDF-");
    }

    /// <summary>
    /// Certification both publishes and consumes the request, so it travels on the host's durable local queue and the
    /// broker never sees it: once a document is stored, the request was handled, and still there is no exchange for it.
    /// </summary>
    [Fact]
    public async Task The_document_request_stays_off_the_broker()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var published = Published();
        await DeliverAsync(PublishedType, published, cancellationToken);
        var passed = await IssueAsync(published, cancellationToken);
        await CertificationDatabase.WaitUntilCountAsync(aspire, StoredByAttempt, passed.AttemptId, 1, cancellationToken);

        await using var connection = await Broker.ConnectAsync(aspire, cancellationToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
        var missing = await Should.ThrowAsync<OperationInterruptedException>(() => channel.ExchangeDeclarePassiveAsync(DocumentRequestedExchange, cancellationToken));

        missing.ShutdownReason.ShouldNotBeNull().ReplyCode.ShouldBe((ushort)404, "the broker holds an exchange for CertificateDocumentRequestedV1");
    }

    [Fact]
    public async Task A_passed_test_redelivered_under_a_new_envelope_and_republished_under_a_new_event_identifier_leaves_one_certificate()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var started = Started(Guid.CreateVersion7(), TimeProvider.System.GetUtcNow());
        var published = Published();
        var passed = Passed(started, published);
        await DeliverAsync(StartedType, started, cancellationToken);
        await DeliverAsync(PublishedType, published, cancellationToken);
        await DeliverAsync(PassedType, passed, cancellationToken);

        // The same event again under another envelope id, so the inbox lets it through to the consumer, then
        // published again by Assessment under an event identifier of its own.
        await DeliverAsync(PassedType, passed, cancellationToken);
        await DeliverAsync(PassedType, passed with { Metadata = NewMetadata() }, cancellationToken);

        (await CountAsync(CertificatesByAttempt, passed.AttemptId, cancellationToken)).ShouldBe(1);
    }

    /// <summary>
    /// Counts on a queue of the test's own, up to a marker certificate issued after the redeliveries. Certification sends
    /// its events in the order it commits them, so the marker's announcement comes after any a redelivery would have sent.
    /// Each attempt is a person of its own, since the announcement does not carry the attempt.
    /// </summary>
    [Fact]
    public async Task An_attempt_delivered_again_is_announced_as_issued_once()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var published = Published();
        await DeliverAsync(PublishedType, published, cancellationToken);

        // A warm-up certificate, so Certification has sent an announcement and Wolverine has declared its exchange,
        // whether or not Compliance's consumer has declared it yet.
        await IssueAsync(published, cancellationToken);
        await using var connection = await Broker.ConnectAsync(aspire, cancellationToken);
        await Broker.WaitForExchangeAsync(connection, CertificateIssuedExchange, cancellationToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
        var queue = await Broker.BindQueueAsync(channel, CertificateIssuedExchange, cancellationToken);

        var passed = await IssueAsync(published, cancellationToken);
        await DeliverAsync(PassedType, passed, cancellationToken);
        await DeliverAsync(PassedType, passed with { Metadata = NewMetadata() }, cancellationToken);
        var marker = await IssueAsync(published, cancellationToken);

        var announced = await Broker.ReceiveUntilAsync<JsonElement>(channel, queue, received => PersonOf(received) == marker.PersonId, cancellationToken);
        var issued = announced.Where(received => PersonOf(received) == passed.PersonId).ShouldHaveSingleItem();
        var certificate = (await CertificatesAsync(passed.AttemptId, cancellationToken)).ShouldHaveSingleItem();
        issued.GetProperty("certificateId").GetGuid().ShouldBe(certificate.Id);
        issued.GetProperty("courseId").GetGuid().ShouldBe(published.CourseId);
        issued.GetProperty("courseVersionId").GetGuid().ShouldBe(published.CourseVersionId);
        issued.GetProperty("issuedAt").GetDateTimeOffset().ShouldBe(PassedAt);
        issued.GetProperty("validUntil").GetString().ShouldBe("2028-03-02");
    }

    /// <summary>
    /// The passed test overtakes the membership: Certification throws, Wolverine retries with backoff, and a retry after
    /// the membership arrived issues the certificate. Waits for the certificate, not for a dead letter.
    /// </summary>
    [Fact]
    public async Task A_passed_test_arriving_before_its_membership_issues_the_certificate_once_the_membership_arrives()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var started = Started(Guid.CreateVersion7(), TimeProvider.System.GetUtcNow());
        var published = Published();
        var passed = Passed(started, published);
        await DeliverAsync(PublishedType, published, cancellationToken);

        await SendAsync(PassedType, passed, Guid.CreateVersion7(), cancellationToken);
        await DeliverAsync(StartedType, started, cancellationToken);

        await CertificationDatabase.WaitUntilCountAsync(aspire, CertificatesByAttempt, passed.AttemptId, 1, cancellationToken);
        (await CertificatesAsync(passed.AttemptId, cancellationToken)).ShouldHaveSingleItem().HolderName.ShouldBe(started.DisplayName);
    }

    /// <summary>
    /// A person in two pharmacies: the certificate carries the name of the membership that started last, although it has
    /// ended and arrived first.
    /// </summary>
    [Fact]
    public async Task The_certificate_carries_the_name_of_the_persons_latest_started_membership_even_after_it_ended()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var personId = Guid.CreateVersion7();
        var now = TimeProvider.System.GetUtcNow();
        var latest = Started(personId, now);
        var earlier = Started(personId, now.AddDays(-30));
        var published = Published();
        var passed = Passed(latest, published);

        await DeliverAsync(StartedType, latest, cancellationToken);
        await DeliverAsync(EndedType, Ended(latest), cancellationToken);
        await DeliverAsync(StartedType, earlier, cancellationToken);
        await DeliverAsync(PublishedType, published, cancellationToken);
        await DeliverAsync(PassedType, passed, cancellationToken);

        (await CertificatesAsync(passed.AttemptId, cancellationToken)).ShouldHaveSingleItem().HolderName.ShouldBe(latest.DisplayName);
    }

    private static Guid PersonOf(JsonElement received) => received.GetProperty("personId").GetGuid();

    private static StartedEvent Started(Guid personId, DateTimeOffset startedAt) =>
        new(Guid.CreateVersion7(), personId, Guid.CreateVersion7(), $"Anna Berger {Guid.CreateVersion7()}", "Pta", "Employee", startedAt, NewMetadata());

    private static EndedEvent Ended(StartedEvent started) =>
        new(started.MembershipId, started.PersonId, started.PharmacyId, TimeProvider.System.GetUtcNow(), NewMetadata());

    private static PublishedEvent Published() =>
        new(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            3,
            $"Hygiene {Guid.CreateVersion7()}",
            12,
            [new SentChapter(Guid.CreateVersion7(), 1, "Grundlagen", "Text", null, "# Grundlagen", null)],
            NewMetadata());

    private static PassedEvent Passed(StartedEvent started, PublishedEvent published) =>
        new(Guid.CreateVersion7(), Guid.CreateVersion7(), started.PersonId, published.CourseId, published.CourseVersionId, 90, PassedAt, NewMetadata());

    private static EventMetadata NewMetadata() => new(Guid.CreateVersion7(), TimeProvider.System.GetUtcNow());

    /// <summary>A person of their own passes the published version, and the certificate is issued.</summary>
    private async Task<PassedEvent> IssueAsync(PublishedEvent published, CancellationToken cancellationToken)
    {
        var started = Started(Guid.CreateVersion7(), TimeProvider.System.GetUtcNow());
        var passed = Passed(started, published);
        await DeliverAsync(StartedType, started, cancellationToken);
        await DeliverAsync(PassedType, passed, cancellationToken);
        (await CountAsync(CertificatesByAttempt, passed.AttemptId, cancellationToken)).ShouldBe(1);
        return passed;
    }

    // The helpers live in CertificationDatabase; these keep the tests above short.
    private Task DeliverAsync<T>(string type, T message, CancellationToken cancellationToken) =>
        CertificationDatabase.DeliverAsync(aspire, type, message, cancellationToken);

    private Task SendAsync<T>(string type, T message, Guid messageId, CancellationToken cancellationToken) =>
        CertificationDatabase.SendAsync(aspire, type, message, messageId, cancellationToken);

    private Task<long> CountAsync(string query, Guid id, CancellationToken cancellationToken) =>
        CertificationDatabase.CountAsync(aspire, query, id, cancellationToken);

    private async Task<List<CertificateRow>> CertificatesAsync(Guid attemptId, CancellationToken cancellationToken)
    {
        await using var connection = await CertificationDatabase.OpenAsync(aspire, cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            select id, holder_id, holder_name, course_id, course_version_id, course_title, course_version_number, score_percent, issued_at, valid_until, verification_code, document_state, document_location
            from certification.certificates where attempt_id = @id
            """,
            connection);
        command.Parameters.AddWithValue("id", attemptId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        List<CertificateRow> rows = [];
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new CertificateRow(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetString(2),
                reader.GetGuid(3),
                reader.GetGuid(4),
                reader.GetString(5),
                reader.GetInt32(6),
                reader.GetInt32(7),
                reader.GetFieldValue<DateTimeOffset>(8),
                reader.GetFieldValue<DateOnly>(9),
                reader.GetString(10),
                reader.GetString(11),
                reader.IsDBNull(12) ? null : reader.GetString(12)));
        }

        return rows;
    }

    /// <summary>Certification's own container, read as its service reads it.</summary>
    private async Task<AzureStoredBlobs> CertificationBlobsAsync(CancellationToken cancellationToken)
    {
        var connectionString = await aspire.App.GetConnectionStringAsync("certification-blobs", cancellationToken)
            ?? throw new InvalidOperationException("Certification's container has no connection string.");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:blobs"] = connectionString })
            .Build();
        return new AzureStoredBlobs(configuration);
    }

    private sealed record EventMetadata(Guid EventIdentifier, DateTimeOffset OccurredAt);

    private sealed record StartedEvent(Guid MembershipId, Guid PersonId, Guid PharmacyId, string DisplayName, string JobFunction, string Role, DateTimeOffset StartedAt, EventMetadata Metadata);

    private sealed record EndedEvent(Guid MembershipId, Guid PersonId, Guid PharmacyId, DateTimeOffset EndedAt, EventMetadata Metadata);

    private sealed record SentChapter(Guid ChapterId, int Position, string Title, string Kind, int? VideoDurationSeconds, string? Text, string? File);

    private sealed record PublishedEvent(Guid CourseId, Guid CourseVersionId, int VersionNumber, string Title, int ValidityMonths, IReadOnlyList<SentChapter> Chapters, EventMetadata Metadata);

    private sealed record PassedEvent(Guid AttemptId, Guid SessionId, Guid PersonId, Guid CourseId, Guid CourseVersionId, int ScorePercent, DateTimeOffset PassedAt, EventMetadata Metadata);

    private sealed record CertificateRow(
        Guid Id,
        Guid HolderId,
        string HolderName,
        Guid CourseId,
        Guid CourseVersionId,
        string CourseTitle,
        int CourseVersionNumber,
        int ScorePercent,
        DateTimeOffset IssuedAt,
        DateOnly ValidUntil,
        string VerificationCode,
        string DocumentState,
        string? DocumentLocation);
}
