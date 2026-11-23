using System.Text.Json;
using Npgsql;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// Issuing through the running application: Organisation's memberships and Editorial's course versions reach
/// Certification's local copies, and Assessment's passed test becomes exactly one certificate for its attempt, however
/// often it arrives, announced once as <c>CertificateIssuedV1</c>. Certification answers none of it yet, so its tables
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

    // 23:30 UTC on 1 March 2027 is already 2 March in Germany; a yearly course is valid through 2 March 2028.
    private static readonly DateTimeOffset PassedAt = new(2027, 3, 1, 23, 30, 0, TimeSpan.Zero);
    private static readonly DateOnly LastValidDay = new(2028, 3, 2);

    [Fact]
    public async Task A_passed_test_issues_one_certificate_with_the_holders_name_the_course_title_and_its_last_valid_day()
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
        certificate.VerificationCode.Length.ShouldBe(32);
        certificate.DocumentState.ShouldBe("Pending");
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
            1,
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
            select id, holder_id, holder_name, course_id, course_version_id, course_title, score_percent, issued_at, valid_until, verification_code, document_state
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
                reader.GetFieldValue<DateTimeOffset>(7),
                reader.GetFieldValue<DateOnly>(8),
                reader.GetString(9),
                reader.GetString(10)));
        }

        return rows;
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
        int ScorePercent,
        DateTimeOffset IssuedAt,
        DateOnly ValidUntil,
        string VerificationCode,
        string DocumentState);
}
