using System.Net;
using System.Net.Http.Json;
using static SmartSolutionsLab.PharmaTraining.Integration.Tests.CertificationIssuing;
using static SmartSolutionsLab.PharmaTraining.Integration.Tests.LearningSessions;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// A person lists their own certificates through the edge (#295): <c>GET /certification/me/certificates</c> answers a
/// page of the caller's certificates, newest first, each with its course title, German day of issue, last valid day,
/// validity today and whether its PDF is ready. Nobody else's certificate is ever in it, and one stays in it after the
/// membership it was earned in has ended: certificates outlive memberships.
/// </summary>
public class CertificationMyCertificatesTests(AspireFixture aspire)
{
    private static readonly TimeZoneInfo GermanTime = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    // Rendering waits on Wolverine's local queue, and a first render loads the fonts; a minute is plenty.
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    [Fact]
    public async Task Without_a_token_it_answers_401()
    {
        using var client = aspire.CreateHostClient();

        using var response = await client.GetAsync(Mine(), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_page_size_above_the_maximum_answers_400()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("developer");

        using var response = await client.GetAsync(new Uri("/certification/me/certificates?pageSize=101", UriKind.Relative), cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync(cancellationToken));
    }

    [Fact]
    public async Task The_callers_certificates_are_listed_newest_first_with_their_days_validity_and_document_state()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var holder = await aspire.CreateHostClientAsync("developer");
        var now = TimeProvider.System.GetUtcNow();
        var started = Started(SubjectOf(holder), now);
        var older = await IssueToAsync(aspire, started, 12, now.AddYears(-2), cancellationToken);
        var newer = await IssueToAsync(aspire, started, 12, now.AddDays(-1), cancellationToken);

        var page = await ListAsync(holder, cancellationToken);

        page.Page.ShouldBe(1);
        page.PageSize.ShouldBe(100);
        page.TotalCount.ShouldBeGreaterThanOrEqualTo(2);
        var ids = page.Items.Select(item => item.CertificateId).ToList();
        ids.ShouldContain(older.Id);
        ids.ShouldContain(newer.Id);
        ids.IndexOf(newer.Id).ShouldBeLessThan(ids.IndexOf(older.Id), "newest first");

        var expired = page.Items.Single(item => item.CertificateId == older.Id);
        expired.CourseTitle.ShouldBe(older.CourseTitle);
        expired.IssuedOn.ShouldBe(DayInGermany(older.IssuedAt));
        expired.ValidUntil.ShouldBe(older.ValidUntil);
        expired.Validity.ShouldBe("Expired");

        var valid = page.Items.Single(item => item.CertificateId == newer.Id);
        valid.CourseTitle.ShouldBe(newer.CourseTitle);
        valid.IssuedOn.ShouldBe(DayInGermany(newer.IssuedAt));
        valid.ValidUntil.ShouldBe(newer.ValidUntil);
        valid.Validity.ShouldBe("Valid");
    }

    [Fact]
    public async Task A_certificate_is_listed_document_ready_once_its_pdf_is_stored()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var holder = await aspire.CreateHostClientAsync("developer");
        var now = TimeProvider.System.GetUtcNow();
        var certificate = await IssueToAsync(aspire, Started(SubjectOf(holder), now), 12, now.AddHours(-1), cancellationToken);
        await WaitUntilStoredAsync(certificate.Id, cancellationToken);

        var page = await ListAsync(holder, cancellationToken);

        page.Items.Single(item => item.CertificateId == certificate.Id).DocumentReady.ShouldBeTrue();
    }

    [Fact]
    public async Task Certificates_issued_at_the_same_instant_are_listed_by_id_descending()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var holder = await aspire.CreateHostClientAsync("developer");
        var now = TimeProvider.System.GetUtcNow();
        var issuedAt = new DateTimeOffset(now.Year - 1, 6, 15, 10, 0, 0, TimeSpan.Zero).AddTicks(Random.Shared.Next(1, 1_000_000) * TimeSpan.TicksPerMillisecond);
        var started = Started(SubjectOf(holder), now);
        var first = await IssueToAsync(aspire, started, 24, issuedAt, cancellationToken);
        var second = await IssueToAsync(aspire, started, 24, issuedAt, cancellationToken);

        var page = await ListAsync(holder, cancellationToken);

        // PostgreSQL orders uuids byte by byte, as their lowercase hex text sorts.
        var expected = new[] { first.Id, second.Id }.OrderByDescending(id => id.ToString("N"), StringComparer.Ordinal).ToList();
        page.Items.Select(item => item.CertificateId).Where(expected.Contains).ShouldBe(expected);
    }

    [Fact]
    public async Task Someone_elses_certificates_are_never_in_the_callers_list()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var holder = await aspire.CreateHostClientAsync("developer");
        using var visitor = await aspire.CreateHostClientAsync("visitor");
        var now = TimeProvider.System.GetUtcNow();
        var holders = await IssueToAsync(aspire, Started(SubjectOf(holder), now), 12, now.AddDays(-3), cancellationToken);
        var strangers = await IssueAsync(aspire, 12, now.AddDays(-3), cancellationToken);

        var holdersPage = await ListAsync(holder, cancellationToken);
        var visitorsPage = await ListAsync(visitor, cancellationToken);

        holdersPage.Items.Select(item => item.CertificateId).ShouldContain(holders.Id);
        holdersPage.Items.Select(item => item.CertificateId).ShouldNotContain(strangers.Id);
        visitorsPage.Items.Select(item => item.CertificateId).ShouldNotContain(holders.Id);
        visitorsPage.Items.Select(item => item.CertificateId).ShouldNotContain(strangers.Id);
    }

    [Fact]
    public async Task A_certificate_is_still_listed_after_the_membership_it_was_earned_in_ended()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var holder = await aspire.CreateHostClientAsync("developer");
        var now = TimeProvider.System.GetUtcNow();
        var started = Started(SubjectOf(holder), now);
        var certificate = await IssueToAsync(aspire, started, 12, now.AddDays(-2), cancellationToken);

        await CertificationDatabase.DeliverAsync(aspire, EndedType, Ended(started), cancellationToken);
        var page = await ListAsync(holder, cancellationToken);

        page.Items.Select(item => item.CertificateId).ShouldContain(certificate.Id);
    }

    private static Uri Mine(int page = 1) => new($"/certification/me/certificates?page={page}&pageSize=100", UriKind.Relative);

    private static DateOnly DayInGermany(DateTimeOffset instant) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, GermanTime).DateTime);

    /// <summary>
    /// Every page of the caller's list, walked to its total, so a test sees all of the caller's certificates however many
    /// other tests issued to them; answers them as one page, with the first page's number, size and total.
    /// </summary>
    private static async Task<CertificatesPage> ListAsync(HttpClient client, CancellationToken cancellationToken)
    {
        var first = await PageAsync(client, 1, cancellationToken);
        List<CertificateBody> items = [.. first.Items];
        for (var page = 2; items.Count < first.TotalCount; page++)
        {
            var next = await PageAsync(client, page, cancellationToken);
            if (next.Items.Count == 0)
            {
                break;
            }

            items.AddRange(next.Items);
        }

        return first with { Items = items };
    }

    private static async Task<CertificatesPage> PageAsync(HttpClient client, int page, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(Mine(page), cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, $"GET {Mine(page)} answered {(int)response.StatusCode} {await response.Content.ReadAsStringAsync(cancellationToken)}");
        return (await response.Content.ReadFromJsonAsync<CertificatesPage>(cancellationToken)).ShouldNotBeNull();
    }

    /// <summary>Waits until Certification has rendered and stored the certificate's PDF, for at most a minute.</summary>
    private async Task WaitUntilStoredAsync(Guid certificateId, CancellationToken cancellationToken)
    {
        await using var connection = await CertificationDatabase.OpenAsync(aspire, cancellationToken);
        var started = TimeProvider.System.GetTimestamp();
        while (true)
        {
            await using var command = new Npgsql.NpgsqlCommand("select document_state from certification.certificates where id = @id", connection);
            command.Parameters.AddWithValue("id", certificateId);
            var state = await command.ExecuteScalarAsync(cancellationToken) as string;
            if (state == "Stored")
            {
                return;
            }

            if (TimeProvider.System.GetElapsedTime(started) >= WaitTimeout)
            {
                throw new TimeoutException($"Certificate {certificateId} was {state ?? "missing"}, not Stored, after {WaitTimeout.TotalSeconds} s.");
            }

            await Task.Delay(PollInterval, cancellationToken);
        }
    }

    private sealed record CertificatesPage(List<CertificateBody> Items, int TotalCount, int Page, int PageSize);

    private sealed record CertificateBody(Guid CertificateId, string CourseTitle, DateOnly IssuedOn, DateOnly ValidUntil, string Validity, bool DocumentReady);
}
