using System.Net;
using Microsoft.Extensions.Time.Testing;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Application.DTOs;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Application.Queries.ListMyCertificates;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Certificates;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Tests.Certificates;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Tests.Queries;

/// <summary>
/// A person lists their own certificates (#295), a page at a time and newest first: each with its course title, the
/// German day of issue, its last valid day, whether it is valid on today's German day and whether its PDF is stored.
/// Only the caller's certificates are counted and listed; a caller who is no person is forbidden.
/// </summary>
public class ListMyCertificatesTests
{
    // 23:59 on 2 March 2028 in Germany: the last valid day of a yearly certificate passed at 23:30 UTC on 1 March 2027.
    private static readonly DateTimeOffset LastValidMinute = new(2028, 3, 2, 22, 59, 0, TimeSpan.Zero);

    private readonly InMemoryCertificateStore certificates = new();
    private readonly FakeTimeProvider time = new(LastValidMinute);
    private readonly PersonId caller = PersonId.From(Guid.CreateVersion7());

    [Fact]
    public async Task Only_the_callers_certificates_are_listed_and_counted()
    {
        var mine = Stored(caller, "Hygiene 2027", TestCertificates.PassedAt);
        Stored(PersonId.From(Guid.CreateVersion7()), "Someone else's", TestCertificates.PassedAt);

        var listed = await ListAsync(caller.Value, Page.First, PageSize.Default);

        listed.IsSuccess.ShouldBeTrue(listed.IsFailure ? listed.Error.Code : string.Empty);
        listed.Value.Items.Select(item => item.CertificateId).ShouldBe([mine.Id.Value]);
        listed.Value.TotalCount.ShouldBe(1);
        listed.Value.Page.ShouldBe(1);
        listed.Value.PageSize.ShouldBe(PageSize.Default.Value);
    }

    [Fact]
    public async Task The_certificates_are_listed_newest_first()
    {
        var oldest = Stored(caller, "Hygiene 2025", new DateTimeOffset(2025, 3, 1, 9, 0, 0, TimeSpan.Zero));
        var newest = Stored(caller, "Hygiene 2027", new DateTimeOffset(2027, 3, 1, 9, 0, 0, TimeSpan.Zero));
        var middle = Stored(caller, "Hygiene 2026", new DateTimeOffset(2026, 3, 1, 9, 0, 0, TimeSpan.Zero));

        var listed = await ListAsync(caller.Value, Page.First, PageSize.Default);

        listed.Value.Items.Select(item => item.CourseTitle).ShouldBe(["Hygiene 2027", "Hygiene 2026", "Hygiene 2025"]);
        listed.Value.Items.Select(item => item.CertificateId).ShouldBe([newest.Id.Value, middle.Id.Value, oldest.Id.Value]);
    }

    [Fact]
    public async Task The_second_page_of_two_holds_the_next_certificates_and_the_count_is_of_the_callers_alone()
    {
        for (var month = 1; month <= 5; month++)
        {
            Stored(caller, $"Course {month}", new DateTimeOffset(2027, month, 1, 9, 0, 0, TimeSpan.Zero));
        }

        Stored(PersonId.From(Guid.CreateVersion7()), "Someone else's", new DateTimeOffset(2027, 2, 15, 9, 0, 0, TimeSpan.Zero));

        var listed = await ListAsync(caller.Value, PageOf(2), PageSizeOf(2));

        listed.Value.Items.Select(item => item.CourseTitle).ShouldBe(["Course 3", "Course 2"]);
        listed.Value.TotalCount.ShouldBe(5);
        listed.Value.Page.ShouldBe(2);
        listed.Value.PageSize.ShouldBe(2);
    }

    [Fact]
    public async Task A_page_past_the_last_is_empty_but_still_counts_them_all()
    {
        Stored(caller, "Hygiene 2027", TestCertificates.PassedAt);

        var listed = await ListAsync(caller.Value, PageOf(3), PageSizeOf(2));

        listed.Value.Items.ShouldBeEmpty();
        listed.Value.TotalCount.ShouldBe(1);
    }

    [Fact]
    public async Task Someone_without_certificates_gets_an_empty_first_page()
    {
        Stored(PersonId.From(Guid.CreateVersion7()), "Someone else's", TestCertificates.PassedAt);

        var listed = await ListAsync(caller.Value, Page.First, PageSize.Default);

        listed.IsSuccess.ShouldBeTrue();
        listed.Value.Items.ShouldBeEmpty();
        listed.Value.TotalCount.ShouldBe(0);
    }

    [Fact]
    public async Task Each_certificate_carries_its_course_title_german_issue_day_last_valid_day_and_validity()
    {
        var certificate = Stored(caller, "Hygiene 2027", TestCertificates.PassedAt);

        var listed = await ListAsync(caller.Value, Page.First, PageSize.Default);

        listed.Value.Items.ShouldHaveSingleItem().ShouldBe(new CertificateDto(
            certificate.Id.Value,
            "Hygiene 2027",
            new DateOnly(2027, 3, 2),
            new DateOnly(2028, 3, 2),
            CertificateValidity.Valid,
            false));
    }

    [Fact]
    public async Task A_certificate_is_valid_through_the_last_minute_of_its_last_german_day()
    {
        Stored(caller, "Hygiene 2027", TestCertificates.PassedAt);

        var listed = await ListAsync(caller.Value, Page.First, PageSize.Default);

        listed.Value.Items.ShouldHaveSingleItem().Validity.ShouldBe(CertificateValidity.Valid);
    }

    [Fact]
    public async Task A_certificate_is_expired_from_midnight_in_germany_after_its_last_valid_day()
    {
        Stored(caller, "Hygiene 2027", TestCertificates.PassedAt);
        time.Advance(TimeSpan.FromMinutes(1)); // 23:00 UTC is 00:00 on 3 March 2028 in Germany

        var listed = await ListAsync(caller.Value, Page.First, PageSize.Default);

        listed.Value.Items.ShouldHaveSingleItem().Validity.ShouldBe(CertificateValidity.Expired);
    }

    [Fact]
    public async Task A_certificate_whose_pdf_is_pending_is_not_document_ready()
    {
        Stored(caller, "Hygiene 2027", TestCertificates.PassedAt);

        var listed = await ListAsync(caller.Value, Page.First, PageSize.Default);

        listed.Value.Items.ShouldHaveSingleItem().DocumentReady.ShouldBeFalse();
    }

    [Fact]
    public async Task A_certificate_whose_pdf_is_stored_is_document_ready()
    {
        var certificate = Stored(caller, "Hygiene 2027", TestCertificates.PassedAt);
        certificate.AttachDocument(DocumentReference.From($"certificates/{certificate.Id.Value:D}.pdf"));

        var listed = await ListAsync(caller.Value, Page.First, PageSize.Default);

        listed.Value.Items.ShouldHaveSingleItem().DocumentReady.ShouldBeTrue();
    }

    [Fact]
    public async Task A_caller_who_is_no_person_is_forbidden()
    {
        Stored(caller, "Hygiene 2027", TestCertificates.PassedAt);

        var listed = await ListAsync(Guid.Empty, Page.First, PageSize.Default);

        listed.IsFailure.ShouldBeTrue();
        listed.Error.Code.ShouldBe("certification.identity-incomplete");
        listed.Error.Status.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task No_query_is_refused()
    {
        await Should.ThrowAsync<GuardException>(() => new ListMyCertificatesHandler(certificates, time).HandleAsync(null!, TestContext.Current.CancellationToken));
    }

    private static Page PageOf(int value) => Page.TryFrom(value, out var page) ? page : throw new InvalidOperationException($"No page {value}.");

    private static PageSize PageSizeOf(int value) => PageSize.TryFrom(value, out var pageSize) ? pageSize : throw new InvalidOperationException($"No page size {value}.");

    private Task<Result<PagedResult<CertificateDto>, ApiError>> ListAsync(Guid person, Page page, PageSize pageSize) =>
        new ListMyCertificatesHandler(certificates, time).HandleAsync(new ListMyCertificates(person, page, pageSize), TestContext.Current.CancellationToken);

    private Certificate Stored(PersonId holder, string courseTitle, DateTimeOffset passedAt)
    {
        var certificate = TestCertificates.Issued(holder, courseTitle, passedAt);
        certificates.Stored.Add(certificate);
        return certificate;
    }
}
