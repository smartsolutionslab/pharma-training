using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using Npgsql;
using static SmartSolutionsLab.PharmaTraining.Integration.Tests.CertificationIssuing;
using static SmartSolutionsLab.PharmaTraining.Integration.Tests.EditorialWorkflow;
using static SmartSolutionsLab.PharmaTraining.Integration.Tests.LearningSessions;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// A manager exports their pharmacy's certificates for a period through the edge (#52):
/// <c>GET /certification/pharmacies/{id}/certificates/export?from=…&amp;to=…</c> answers a ZIP holding <c>index.csv</c>
/// and each stored PDF. Who may is decided from Certification's own copy of the caller's membership and its current role
/// (docs/adr/0003): a non-member gets 404, an employee 403. A certificate is exported when its holder was a member of the
/// pharmacy when it was issued and its issue day in Germany lies within the period.
/// </summary>
public class CertificationExportTests(AspireFixture aspire)
{
    private const string StoredOfHolder = "select count(*) from certification.certificates where holder_id = @id and document_state = 'Stored'";

    private static readonly DateTimeOffset LongAgo = new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Without_a_token_it_answers_401()
    {
        using var client = aspire.CreateHostClient();

        using var response = await client.GetAsync(Export(Guid.CreateVersion7(), "?from=2025-01-01&to=2025-12-31"), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Someone_who_is_no_member_of_the_pharmacy_gets_404()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var developer = await aspire.CreateHostClientAsync("developer");

        using var response = await developer.GetAsync(Export(Guid.CreateVersion7(), "?from=2025-01-01&to=2025-12-31"), cancellationToken);

        await ShouldBeRefusedAsync(response, HttpStatusCode.NotFound, "certification.pharmacy-not-found", cancellationToken);
    }

    [Fact]
    public async Task An_employee_of_the_pharmacy_gets_403()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var developer = await aspire.CreateHostClientAsync("developer");
        var pharmacy = Guid.CreateVersion7();
        await CertificationDatabase.DeliverAsync(aspire, StartedType, StartedIn(SubjectOf(developer), pharmacy, "Employee", LongAgo), cancellationToken);

        using var response = await developer.GetAsync(Export(pharmacy, "?from=2025-01-01&to=2025-12-31"), cancellationToken);

        await ShouldBeRefusedAsync(response, HttpStatusCode.Forbidden, "certification.capability-missing", cancellationToken);
    }

    [Fact]
    public async Task A_manager_demoted_by_a_membership_change_gets_403()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var developer = await aspire.CreateHostClientAsync("developer");
        var pharmacy = Guid.CreateVersion7();
        var started = StartedIn(SubjectOf(developer), pharmacy, "Manager", LongAgo);
        await CertificationDatabase.DeliverAsync(aspire, StartedType, started, cancellationToken);
        using (var before = await developer.GetAsync(Export(pharmacy, "?from=2025-01-01&to=2025-12-31"), cancellationToken))
        {
            before.StatusCode.ShouldBe(HttpStatusCode.OK, await before.Content.ReadAsStringAsync(cancellationToken));
        }

        await CertificationDatabase.DeliverAsync(aspire, ChangedType, Changed(started, "Employee", 2), cancellationToken);
        using var response = await developer.GetAsync(Export(pharmacy, "?from=2025-01-01&to=2025-12-31"), cancellationToken);

        await ShouldBeRefusedAsync(response, HttpStatusCode.Forbidden, "certification.capability-missing", cancellationToken);
    }

    [Theory]
    [InlineData("?from=2025-06-02&to=2025-06-01")]
    [InlineData("?from=2020-01-01&to=2025-01-02")]
    [InlineData("?from=2025-01-01")]
    [InlineData("?to=2025-01-01")]
    [InlineData("")]
    public async Task A_period_outside_the_rules_gets_400(string query)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var developer = await aspire.CreateHostClientAsync("developer");
        var pharmacy = await ManagedPharmacyAsync(aspire, developer, cancellationToken);

        using var response = await developer.GetAsync(Export(pharmacy, query), cancellationToken);

        await ShouldBeRefusedAsync(response, HttpStatusCode.BadRequest, "certification.invalid-export-period", cancellationToken);
    }

    [Fact]
    public async Task A_date_that_is_no_date_gets_400()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var developer = await aspire.CreateHostClientAsync("developer");
        var pharmacy = await ManagedPharmacyAsync(aspire, developer, cancellationToken);

        using var response = await developer.GetAsync(Export(pharmacy, "?from=abc&to=2025-12-31"), cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync(cancellationToken));
    }

    [Fact]
    public async Task A_period_without_certificates_is_a_zip_holding_the_index_header_alone()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var developer = await aspire.CreateHostClientAsync("developer");
        var pharmacy = await ManagedPharmacyAsync(aspire, developer, cancellationToken);

        var export = await ExportAsync(developer, pharmacy, "2025-01-01", "2025-12-31", cancellationToken);

        export.Entries.ShouldHaveSingleItem().Name.ShouldBe("index.csv");
        export.IndexRows.ShouldHaveSingleItem().ShouldStartWith("Bestätigungs-Id;Name;Schulung;");
    }

    /// <summary>
    /// Done when (A): a former member worked in the pharmacy from 1 March to 1 September 2025. Of the certificates they
    /// passed on 15 February, 10 May and 1 October, only the one from 10 May is in the export, in the index and as a PDF.
    /// </summary>
    [Fact]
    public async Task Certificates_issued_before_or_after_the_membership_are_not_exported()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var developer = await aspire.CreateHostClientAsync("developer");
        var pharmacy = await ManagedPharmacyAsync(aspire, developer, cancellationToken);
        var member = StartedIn(Guid.CreateVersion7(), pharmacy, "Employee", new DateTimeOffset(2025, 3, 1, 8, 0, 0, TimeSpan.Zero));
        await CertificationDatabase.DeliverAsync(aspire, StartedType, member, cancellationToken);
        await CertificationDatabase.DeliverAsync(aspire, EndedType, EndedAt(member, new DateTimeOffset(2025, 9, 1, 8, 0, 0, TimeSpan.Zero)), cancellationToken);
        var published = Published();
        await CertificationDatabase.DeliverAsync(aspire, PublishedType, published, cancellationToken);
        var before = await PassAsync(member, published, new DateTimeOffset(2025, 2, 15, 10, 0, 0, TimeSpan.Zero), cancellationToken);
        var during = await PassAsync(member, published, new DateTimeOffset(2025, 5, 10, 10, 0, 0, TimeSpan.Zero), cancellationToken);
        var after = await PassAsync(member, published, new DateTimeOffset(2025, 10, 1, 10, 0, 0, TimeSpan.Zero), cancellationToken);
        await CertificationDatabase.WaitUntilCountAsync(aspire, StoredOfHolder, member.PersonId, 3, cancellationToken);

        var export = await ExportAsync(developer, pharmacy, "2025-01-01", "2025-12-31", cancellationToken);

        export.ExportedIds().ShouldBe([during]);
        export.Entries.Select(entry => entry.Name).ShouldBe(["index.csv", $"pdf/{during:D}.pdf"]);
        export.ShouldNotMention(before);
        export.ShouldNotMention(after);
        export.PdfsShouldBePdfs();
    }

    /// <summary>
    /// Done when (B): a member's certificates passed at 22:30 UTC on 30 April 2025 (1 May in Germany), at 21:30 UTC on
    /// 30 June (still 30 June in Germany) and at noon UTC on 1 July. The export from 1 May to 30 June holds the first two.
    /// </summary>
    [Fact]
    public async Task Certificates_issued_outside_from_and_to_are_not_exported()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var developer = await aspire.CreateHostClientAsync("developer");
        var pharmacy = await ManagedPharmacyAsync(aspire, developer, cancellationToken);
        var member = StartedIn(Guid.CreateVersion7(), pharmacy, "Employee", LongAgo);
        await CertificationDatabase.DeliverAsync(aspire, StartedType, member, cancellationToken);
        var published = Published();
        await CertificationDatabase.DeliverAsync(aspire, PublishedType, published, cancellationToken);
        var firstDay = await PassAsync(member, published, new DateTimeOffset(2025, 4, 30, 22, 30, 0, TimeSpan.Zero), cancellationToken);
        var lastDay = await PassAsync(member, published, new DateTimeOffset(2025, 6, 30, 21, 30, 0, TimeSpan.Zero), cancellationToken);
        var outside = await PassAsync(member, published, new DateTimeOffset(2025, 7, 1, 12, 0, 0, TimeSpan.Zero), cancellationToken);
        await CertificationDatabase.WaitUntilCountAsync(aspire, StoredOfHolder, member.PersonId, 3, cancellationToken);

        var export = await ExportAsync(developer, pharmacy, "2025-05-01", "2025-06-30", cancellationToken);

        export.ExportedIds().ShouldBe([firstDay, lastDay]);
        export.Entries.Select(entry => entry.Name).ShouldBe(["index.csv", $"pdf/{firstDay:D}.pdf", $"pdf/{lastDay:D}.pdf"]);
        export.ShouldNotMention(outside);
        export.IndexRows[1].Split(';')[4].ShouldBe("2025-05-01");
        export.IndexRows[2].Split(';')[4].ShouldBe("2025-06-30");
        export.PdfsShouldBePdfs();
    }

    /// <summary>
    /// The membership rule as the database applies it (#367 moves it into the query): a member who worked in the pharmacy
    /// from 1 March to 1 May 2025 and again from 1 August. Of their certificates passed exactly at the first start, exactly
    /// at the first end, between the two memberships and during the second, the first and the last are exported. A member
    /// of another pharmacy only, and a person of whom only an ending (no start) reached this pharmacy, have none exported.
    /// </summary>
    [Fact]
    public async Task Only_certificates_from_a_membership_in_this_pharmacy_are_exported_start_inclusive_end_exclusive()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var developer = await aspire.CreateHostClientAsync("developer");
        var pharmacy = await ManagedPharmacyAsync(aspire, developer, cancellationToken);
        var firstStart = new DateTimeOffset(2025, 3, 1, 8, 0, 0, TimeSpan.Zero);
        var firstEnd = new DateTimeOffset(2025, 5, 1, 8, 0, 0, TimeSpan.Zero);
        var first = StartedIn(Guid.CreateVersion7(), pharmacy, "Employee", firstStart);
        var second = StartedIn(first.PersonId, pharmacy, "Employee", new DateTimeOffset(2025, 8, 1, 8, 0, 0, TimeSpan.Zero));
        await CertificationDatabase.DeliverAsync(aspire, StartedType, first, cancellationToken);
        await CertificationDatabase.DeliverAsync(aspire, EndedType, EndedAt(first, firstEnd), cancellationToken);
        await CertificationDatabase.DeliverAsync(aspire, StartedType, second, cancellationToken);
        var elsewhere = StartedIn(Guid.CreateVersion7(), Guid.CreateVersion7(), "Employee", LongAgo);
        await CertificationDatabase.DeliverAsync(aspire, StartedType, elsewhere, cancellationToken);
        var endedOnly = new EndedEvent(Guid.CreateVersion7(), Guid.CreateVersion7(), pharmacy, new DateTimeOffset(2025, 12, 1, 8, 0, 0, TimeSpan.Zero), NewMetadata());
        await CertificationDatabase.DeliverAsync(aspire, EndedType, endedOnly, cancellationToken);

        // Certification names a certificate's holder after one of their memberships, so this person has one elsewhere.
        var namedElsewhere = StartedIn(endedOnly.PersonId, Guid.CreateVersion7(), "Employee", LongAgo);
        await CertificationDatabase.DeliverAsync(aspire, StartedType, namedElsewhere, cancellationToken);
        var published = Published();
        await CertificationDatabase.DeliverAsync(aspire, PublishedType, published, cancellationToken);
        var atStart = await PassAsync(first, published, firstStart, cancellationToken);
        var atEnd = await PassAsync(first, published, firstEnd, cancellationToken);
        var between = await PassAsync(first, published, new DateTimeOffset(2025, 6, 15, 10, 0, 0, TimeSpan.Zero), cancellationToken);
        var during = await PassAsync(first, published, new DateTimeOffset(2025, 9, 1, 10, 0, 0, TimeSpan.Zero), cancellationToken);
        var ofElsewhere = await PassAsync(elsewhere, published, new DateTimeOffset(2025, 6, 15, 10, 0, 0, TimeSpan.Zero), cancellationToken);
        var ofEndedOnly = await PassAsync(namedElsewhere, published, new DateTimeOffset(2025, 6, 15, 10, 0, 0, TimeSpan.Zero), cancellationToken);
        await CertificationDatabase.WaitUntilCountAsync(aspire, StoredOfHolder, first.PersonId, 4, cancellationToken);

        var export = await ExportAsync(developer, pharmacy, "2025-01-01", "2025-12-31", cancellationToken);

        export.ExportedIds().ShouldBe([atStart, during]);
        export.Entries.Select(entry => entry.Name).ShouldBe(["index.csv", $"pdf/{atStart:D}.pdf", $"pdf/{during:D}.pdf"]);
        export.ShouldNotMention(atEnd);
        export.ShouldNotMention(between);
        export.ShouldNotMention(ofElsewhere);
        export.ShouldNotMention(ofEndedOnly);
        export.PdfsShouldBePdfs();
    }

    /// <summary>
    /// #367: a row marked stored without a location (the domain never writes it, but nothing in the database refuses it)
    /// has no PDF in the archive, so the index says its PDF is pending, as the single download does.
    /// </summary>
    [Fact]
    public async Task A_certificate_marked_stored_without_a_location_is_listed_as_pending_and_has_no_pdf()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var developer = await aspire.CreateHostClientAsync("developer");
        var pharmacy = await ManagedPharmacyAsync(aspire, developer, cancellationToken);
        var member = StartedIn(Guid.CreateVersion7(), pharmacy, "Employee", LongAgo);
        await CertificationDatabase.DeliverAsync(aspire, StartedType, member, cancellationToken);
        var published = Published();
        await CertificationDatabase.DeliverAsync(aspire, PublishedType, published, cancellationToken);
        var certificate = await PassAsync(member, published, new DateTimeOffset(2025, 5, 10, 10, 0, 0, TimeSpan.Zero), cancellationToken);
        await CertificationDatabase.WaitUntilCountAsync(aspire, StoredOfHolder, member.PersonId, 1, cancellationToken);
        await using (var connection = await CertificationDatabase.OpenAsync(aspire, cancellationToken))
        {
            await using var command = new NpgsqlCommand("update certification.certificates set document_location = null where id = @id", connection);
            command.Parameters.AddWithValue("id", certificate);
            (await command.ExecuteNonQueryAsync(cancellationToken)).ShouldBe(1);
        }

        var export = await ExportAsync(developer, pharmacy, "2025-01-01", "2025-12-31", cancellationToken);

        export.ExportedIds().ShouldBe([certificate]);
        export.IndexRows[1].ShouldEndWith(";noch nicht erstellt");
        export.Entries.Select(entry => entry.Name).ShouldNotContain($"pdf/{certificate:D}.pdf");
    }

    /// <summary>
    /// Retention (#53, docs/adr/0034): the pharmacy's export leaves out a certificate issued five years ago or longer,
    /// whatever period is asked for. On the host's own clock, a certificate passed five years and a day ago is not
    /// exported and one passed five years less a day ago is; the exact instant is the unit tests' (FakeTimeProvider).
    /// </summary>
    [Fact]
    public async Task Certificates_issued_five_years_ago_or_longer_are_not_exported()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var developer = await aspire.CreateHostClientAsync("developer");
        var pharmacy = await ManagedPharmacyAsync(aspire, developer, cancellationToken);
        var member = StartedIn(Guid.CreateVersion7(), pharmacy, "Employee", LongAgo);
        await CertificationDatabase.DeliverAsync(aspire, StartedType, member, cancellationToken);
        var published = Published();
        await CertificationDatabase.DeliverAsync(aspire, PublishedType, published, cancellationToken);
        var fiveYearsAgo = TimeProvider.System.GetUtcNow().AddYears(-5);
        var expired = await PassAsync(member, published, fiveYearsAgo.AddDays(-1), cancellationToken);
        var kept = await PassAsync(member, published, fiveYearsAgo.AddDays(1), cancellationToken);
        await CertificationDatabase.WaitUntilCountAsync(aspire, StoredOfHolder, member.PersonId, 2, cancellationToken);

        var export = await ExportAsync(developer, pharmacy, DayOf(fiveYearsAgo.AddDays(-3)), DayOf(fiveYearsAgo.AddDays(3)), cancellationToken);

        export.ExportedIds().ShouldBe([kept]);
        export.Entries.Select(entry => entry.Name).ShouldBe(["index.csv", $"pdf/{kept:D}.pdf"]);
        export.ShouldNotMention(expired);
        export.PdfsShouldBePdfs();
    }

    /// <summary>
    /// Retention as the database applies it (#53 after #367): the query that reads at most 2001 certificates leaves out
    /// the ones the pharmacy no longer keeps, so a period holding 2000 kept certificates and five older than five years is
    /// exported whole, not refused as too large, and without the five. The 2005 rows are copies of one passed certificate,
    /// with their PDFs pending, so the archive holds the index alone.
    /// </summary>
    [Fact]
    public async Task Certificates_older_than_five_years_do_not_count_towards_the_limit()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var developer = await aspire.CreateHostClientAsync("developer");
        var pharmacy = await ManagedPharmacyAsync(aspire, developer, cancellationToken);
        var member = StartedIn(Guid.CreateVersion7(), pharmacy, "Employee", LongAgo);
        await CertificationDatabase.DeliverAsync(aspire, StartedType, member, cancellationToken);
        var published = Published();
        await CertificationDatabase.DeliverAsync(aspire, PublishedType, published, cancellationToken);
        var fiveYearsAgo = TimeProvider.System.GetUtcNow().AddYears(-5);
        var template = await PassAsync(member, published, fiveYearsAgo.AddDays(1), cancellationToken);
        await CertificationDatabase.WaitUntilCountAsync(aspire, StoredOfHolder, member.PersonId, 1, cancellationToken);
        await CopyAsync(template, 2000 - 1, fiveYearsAgo.AddDays(1).AddSeconds(1), cancellationToken);
        await CopyAsync(template, 5, fiveYearsAgo.AddDays(-1), cancellationToken);
        var expired = await IssuedBeforeAsync(member.PersonId, fiveYearsAgo, cancellationToken);

        var export = await ExportAsync(developer, pharmacy, DayOf(fiveYearsAgo.AddDays(-3)), DayOf(fiveYearsAgo.AddDays(3)), cancellationToken);

        expired.Count.ShouldBe(5);
        var exported = export.ExportedIds();
        exported.Count.ShouldBe(2000);
        exported.ShouldContain(template);
        exported.ShouldNotContain(id => expired.Contains(id));
    }

    /// <summary>
    /// #391: the pharmacy's retention period is retroactive. On the host's own clock, a certificate passed six years ago
    /// is left out while the pharmacy keeps the default five years, and is exported once it keeps seven.
    /// </summary>
    [Fact]
    public async Task A_pharmacy_that_keeps_seven_years_exports_a_certificate_issued_six_years_ago()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var developer = await aspire.CreateHostClientAsync("developer");
        var pharmacy = await ManagedPharmacyAsync(aspire, developer, cancellationToken);
        var member = StartedIn(Guid.CreateVersion7(), pharmacy, "Employee", LongAgo);
        await CertificationDatabase.DeliverAsync(aspire, StartedType, member, cancellationToken);
        var published = Published();
        await CertificationDatabase.DeliverAsync(aspire, PublishedType, published, cancellationToken);
        var sixYearsAgo = TimeProvider.System.GetUtcNow().AddYears(-6);
        var certificate = await PassAsync(member, published, sixYearsAgo, cancellationToken);
        await CertificationDatabase.WaitUntilCountAsync(aspire, StoredOfHolder, member.PersonId, 1, cancellationToken);
        var from = DayOf(sixYearsAgo.AddDays(-3));
        var to = DayOf(sixYearsAgo.AddDays(3));

        var before = await ExportAsync(developer, pharmacy, from, to, cancellationToken);
        await KeepAsync(developer, pharmacy, 7, cancellationToken);
        var after = await ExportAsync(developer, pharmacy, from, to, cancellationToken);

        before.ShouldNotMention(certificate);
        after.ExportedIds().ShouldBe([certificate]);
        after.Entries.Select(entry => entry.Name).ShouldBe(["index.csv", $"pdf/{certificate:D}.pdf"]);
        after.PdfsShouldBePdfs();
    }

    /// <summary>
    /// #391: a pharmacy that keeps two years leaves out a certificate passed three years ago, which the default five years
    /// exported, and still exports one passed a year ago.
    /// </summary>
    [Fact]
    public async Task A_pharmacy_that_keeps_two_years_leaves_out_a_certificate_issued_three_years_ago()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var developer = await aspire.CreateHostClientAsync("developer");
        var pharmacy = await ManagedPharmacyAsync(aspire, developer, cancellationToken);
        var member = StartedIn(Guid.CreateVersion7(), pharmacy, "Employee", LongAgo);
        await CertificationDatabase.DeliverAsync(aspire, StartedType, member, cancellationToken);
        var published = Published();
        await CertificationDatabase.DeliverAsync(aspire, PublishedType, published, cancellationToken);
        var now = TimeProvider.System.GetUtcNow();
        var threeYearsOld = await PassAsync(member, published, now.AddYears(-3), cancellationToken);
        var oneYearOld = await PassAsync(member, published, now.AddYears(-1), cancellationToken);
        await CertificationDatabase.WaitUntilCountAsync(aspire, StoredOfHolder, member.PersonId, 2, cancellationToken);
        var from = DayOf(now.AddYears(-3).AddDays(-3));
        var to = DayOf(now);

        var before = await ExportAsync(developer, pharmacy, from, to, cancellationToken);
        await KeepAsync(developer, pharmacy, 2, cancellationToken);
        var after = await ExportAsync(developer, pharmacy, from, to, cancellationToken);

        before.ExportedIds().ShouldBe([threeYearsOld, oneYearOld]);
        after.ExportedIds().ShouldBe([oneYearOld]);
        after.ShouldNotMention(threeYearsOld);
    }

    /// <summary>The manager sets the pharmacy's retention period through the edge (#391); answers once it answered 204.</summary>
    private static async Task KeepAsync(HttpClient manager, Guid pharmacy, int years, CancellationToken cancellationToken)
    {
        using var body = new StringContent($"{{\"years\":{years}}}", Encoding.UTF8, "application/json");
        using var response = await manager.PutAsync(new Uri($"/certification/pharmacies/{pharmacy:D}/retention-period", UriKind.Relative), body, cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync(cancellationToken));
    }

    /// <summary>
    /// Stores <paramref name="count"/> copies of the certificate, one second apart from <paramref name="firstIssuedAt"/> on,
    /// each with its own id, attempt and verification code, and its PDF pending.
    /// </summary>
    private async Task CopyAsync(Guid template, int count, DateTimeOffset firstIssuedAt, CancellationToken cancellationToken)
    {
        await using var connection = await CertificationDatabase.OpenAsync(aspire, cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            insert into certification.certificates (id, attempt_id, holder_id, holder_name, course_id, course_version_id, course_title,
                course_version_number, score_percent, issued_at, valid_until, verification_code, document_state, document_location)
            select gen_random_uuid(), gen_random_uuid(), holder_id, holder_name, course_id, course_version_id, course_title,
                course_version_number, score_percent, @first + (n - 1) * interval '1 second', valid_until,
                md5(random()::text || n::text), 'Pending', null
            from certification.certificates, generate_series(1, @count) as n
            where id = @template
            """,
            connection);
        command.Parameters.AddWithValue("first", firstIssuedAt);
        command.Parameters.AddWithValue("count", count);
        command.Parameters.AddWithValue("template", template);
        (await command.ExecuteNonQueryAsync(cancellationToken)).ShouldBe(count);
    }

    /// <summary>The ids of the holder's certificates issued before the instant.</summary>
    private async Task<List<Guid>> IssuedBeforeAsync(Guid holder, DateTimeOffset instant, CancellationToken cancellationToken)
    {
        await using var connection = await CertificationDatabase.OpenAsync(aspire, cancellationToken);
        await using var command = new NpgsqlCommand("select id from certification.certificates where holder_id = @holder and issued_at < @instant", connection);
        command.Parameters.AddWithValue("holder", holder);
        command.Parameters.AddWithValue("instant", instant);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        List<Guid> ids = [];
        while (await reader.ReadAsync(cancellationToken))
        {
            ids.Add(reader.GetGuid(0));
        }

        return ids;
    }

    private static string DayOf(DateTimeOffset instant) => instant.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    private static Uri Export(Guid pharmacy, string query) => new($"/certification/pharmacies/{pharmacy:D}/certificates/export{query}", UriKind.Relative);

    private static async Task ShouldBeRefusedAsync(HttpResponseMessage response, HttpStatusCode status, string code, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        response.StatusCode.ShouldBe(status, body);
        (await CodeOfAsync(response, cancellationToken)).ShouldBe(code);
        using var problem = JsonDocument.Parse(body);
        problem.RootElement.GetProperty("title").GetString().ShouldBe(ErrorCodeCatalog.MessageOf(code));
    }

    /// <summary>Answers the export of the period as a 200 ZIP, checking its headers, read entry by entry.</summary>
    private static async Task<ExportedZip> ExportAsync(HttpClient client, Guid pharmacy, string from, string to, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(Export(pharmacy, $"?from={from}&to={to}"), cancellationToken);
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, Encoding.UTF8.GetString(bytes));
        response.Content.Headers.ContentType.ShouldNotBeNull().MediaType.ShouldBe("application/zip");
        var disposition = response.Content.Headers.ContentDisposition.ShouldNotBeNull();
        disposition.DispositionType.ShouldBe("attachment");
        (disposition.FileNameStar ?? disposition.FileName?.Trim('"')).ShouldBe($"teilnahmebestaetigungen_{from}_{to}.zip");

        using var zip = new ZipArchive(new MemoryStream(bytes, writable: false), ZipArchiveMode.Read);
        List<(string Name, byte[] Content)> entries = [];
        foreach (var entry in zip.Entries)
        {
            await using var content = await entry.OpenAsync(cancellationToken);
            using var copy = new MemoryStream();
            await content.CopyToAsync(copy, cancellationToken);
            entries.Add((entry.FullName, copy.ToArray()));
        }

        entries.ShouldNotBeEmpty();
        entries[0].Name.ShouldBe("index.csv");
        var index = entries[0].Content;
        index.Take(3).ShouldBe(new byte[] { 0xEF, 0xBB, 0xBF }, "index.csv starts with the UTF-8 byte order mark");
        var text = Encoding.UTF8.GetString(index, 3, index.Length - 3);
        text.ShouldEndWith("\r\n");
        return new ExportedZip(entries, text[..^2].Split("\r\n"));
    }

    /// <summary>The member passes the published version's test at the instant; answers the id of the certificate Certification issued.</summary>
    private async Task<Guid> PassAsync(StartedEvent member, PublishedEvent published, DateTimeOffset passedAt, CancellationToken cancellationToken)
    {
        var passed = Passed(member, published, passedAt);
        await CertificationDatabase.DeliverAsync(aspire, PassedType, passed, cancellationToken);
        return (await CertificatesAsync(aspire, passed.AttemptId, cancellationToken)).ShouldHaveSingleItem().Id;
    }

    private sealed record ExportedZip(List<(string Name, byte[] Content)> Entries, string[] IndexRows)
    {
        /// <summary>The certificate ids of the index's rows, in order, after its header.</summary>
        public List<Guid> ExportedIds() => IndexRows.Skip(1).Select(row => Guid.Parse(row.Split(';')[0])).ToList();

        public void ShouldNotMention(Guid certificate)
        {
            string.Join("\r\n", IndexRows).ShouldNotContain(certificate.ToString("D"));
            Entries.Select(entry => entry.Name).ShouldNotContain($"pdf/{certificate:D}.pdf");
        }

        public void PdfsShouldBePdfs()
        {
            foreach (var (name, content) in Entries.Skip(1))
            {
                Encoding.ASCII.GetString(content, 0, 5).ShouldBe("%PDF-", $"{name} is no PDF");
            }
        }
    }
}
