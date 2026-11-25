using System.Net;
using Microsoft.Extensions.Time.Testing;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Application.Exports;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Application.Queries.ExportPharmacyCertificates;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Certificates;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Memberships;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Tests.Certificates;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Tests.Exports;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Tests.Memberships;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Tests.Queries;

/// <summary>
/// A manager exports the certificates of their pharmacy for a period (#52). The checks run in this order, the first that
/// fails answering: a caller who is a person (403), an active membership of the caller in the pharmacy (404), a current
/// role there that grants the export in every such membership (403), a valid period (400), and at most 2000
/// certificates (400). A certificate is exported when its holder was a member of the pharmacy at the instant it was
/// issued, start inclusive and end exclusive, and its issue day in Germany lies within the period.
/// </summary>
public class ExportPharmacyCertificatesTests
{
    private static readonly DateOnly From = new(2025, 1, 1);
    private static readonly DateOnly To = new(2025, 12, 31);
    private static readonly DateTimeOffset LongAgo = new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly InMemoryMembershipStore memberships = new();
    private readonly InMemoryCertificateStore certificates;
    private readonly InMemoryBlobReads blobs = new();
    private readonly FakeTimeProvider time = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
    private readonly PharmacyId pharmacy = PharmacyId.From(Guid.CreateVersion7());
    private readonly PersonId manager = PersonId.From(Guid.CreateVersion7());

    public ExportPharmacyCertificatesTests()
    {
        certificates = new InMemoryCertificateStore(memberships);
    }

    [Fact]
    public async Task A_caller_who_is_no_person_is_forbidden()
    {
        Start(manager, MembershipRole.Manager, LongAgo);

        var export = await HandleAsync(Guid.Empty);

        ShouldBeRefused(export, HttpStatusCode.Forbidden, "certification.identity-incomplete");
    }

    [Fact]
    public async Task The_empty_pharmacy_id_is_not_found()
    {
        var export = await HandleAsync(new ExportPharmacyCertificates(Guid.Empty, manager.Value, From, To));

        ShouldBeRefused(export, HttpStatusCode.NotFound, "certification.pharmacy-not-found");
    }

    [Fact]
    public async Task Someone_who_is_no_member_is_told_the_pharmacy_is_not_found()
    {
        Start(PersonId.From(Guid.CreateVersion7()), MembershipRole.Manager, LongAgo);

        var export = await HandleAsync(manager.Value);

        ShouldBeRefused(export, HttpStatusCode.NotFound, "certification.pharmacy-not-found");
    }

    [Fact]
    public async Task A_manager_of_another_pharmacy_is_told_the_pharmacy_is_not_found()
    {
        Start(manager, MembershipRole.Manager, LongAgo, PharmacyId.From(Guid.CreateVersion7()));

        var export = await HandleAsync(manager.Value);

        ShouldBeRefused(export, HttpStatusCode.NotFound, "certification.pharmacy-not-found");
    }

    [Fact]
    public async Task A_manager_whose_membership_ended_is_told_the_pharmacy_is_not_found()
    {
        var membership = Start(manager, MembershipRole.Manager, LongAgo);
        End(membership, manager, new DateTimeOffset(2025, 6, 1, 0, 0, 0, TimeSpan.Zero));

        var export = await HandleAsync(manager.Value);

        ShouldBeRefused(export, HttpStatusCode.NotFound, "certification.pharmacy-not-found");
    }

    [Fact]
    public async Task An_employee_is_forbidden()
    {
        Start(manager, MembershipRole.Employee, LongAgo);

        var export = await HandleAsync(manager.Value);

        ShouldBeRefused(export, HttpStatusCode.Forbidden, "certification.capability-missing");
    }

    [Fact]
    public async Task A_manager_demoted_by_a_later_change_is_forbidden()
    {
        var membership = Start(manager, MembershipRole.Manager, LongAgo);
        Change(membership, manager, 2, MembershipRole.Employee);

        var export = await HandleAsync(manager.Value);

        ShouldBeRefused(export, HttpStatusCode.Forbidden, "certification.capability-missing");
    }

    [Fact]
    public async Task An_employee_promoted_by_a_later_change_may_export()
    {
        var membership = Start(manager, MembershipRole.Employee, LongAgo);
        Change(membership, manager, 2, MembershipRole.Manager);

        var export = await HandleAsync(manager.Value);

        export.IsSuccess.ShouldBeTrue(export.IsFailure ? export.Error.Code : string.Empty);
    }

    [Fact]
    public async Task The_newest_change_decides_whatever_order_the_changes_were_stored_in()
    {
        var membership = Start(manager, MembershipRole.Employee, LongAgo);
        Change(membership, manager, 3, MembershipRole.Employee);
        Change(membership, manager, 2, MembershipRole.Manager);

        var export = await HandleAsync(manager.Value);

        ShouldBeRefused(export, HttpStatusCode.Forbidden, "certification.capability-missing");
    }

    /// <summary>
    /// Two active memberships of one person in one pharmacy mean an ending is still on its way; Certification cannot tell
    /// which one ended, so it fails closed: the export is allowed only when every one of them allows it.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Of_two_active_memberships_one_that_does_not_allow_it_withholds_it(bool managerFirst)
    {
        Start(manager, managerFirst ? MembershipRole.Manager : MembershipRole.Employee, LongAgo);
        Start(manager, managerFirst ? MembershipRole.Employee : MembershipRole.Manager, LongAgo.AddDays(1));

        var export = await HandleAsync(manager.Value);

        ShouldBeRefused(export, HttpStatusCode.Forbidden, "certification.capability-missing");
    }

    [Fact]
    public async Task An_ended_employee_membership_beside_an_active_manager_one_allows_it()
    {
        var ended = Start(manager, MembershipRole.Employee, LongAgo);
        End(ended, manager, LongAgo.AddYears(1));
        Start(manager, MembershipRole.Manager, LongAgo.AddYears(2));

        var export = await HandleAsync(manager.Value);

        export.IsSuccess.ShouldBeTrue(export.IsFailure ? export.Error.Code : string.Empty);
    }

    [Theory]
    [InlineData(null, "2025-12-31")]
    [InlineData("2025-01-01", null)]
    [InlineData(null, null)]
    [InlineData("2025-06-02", "2025-06-01")]
    [InlineData("2020-01-01", "2025-01-02")]
    public async Task A_period_outside_the_rules_is_a_bad_request(string? from, string? to)
    {
        Start(manager, MembershipRole.Manager, LongAgo);

        var export = await HandleAsync(new ExportPharmacyCertificates(pharmacy.Value, manager.Value, DateOf(from), DateOf(to)));

        ShouldBeRefused(export, HttpStatusCode.BadRequest, "certification.invalid-export-period");
    }

    [Fact]
    public async Task Exactly_five_years_may_be_exported()
    {
        Start(manager, MembershipRole.Manager, LongAgo);

        var export = await HandleAsync(new ExportPharmacyCertificates(pharmacy.Value, manager.Value, new DateOnly(2020, 1, 1), new DateOnly(2025, 1, 1)));

        export.IsSuccess.ShouldBeTrue(export.IsFailure ? export.Error.Code : string.Empty);
    }

    [Fact]
    public async Task A_non_member_with_an_invalid_period_is_told_the_pharmacy_is_not_found_first()
    {
        var export = await HandleAsync(new ExportPharmacyCertificates(pharmacy.Value, manager.Value, null, null));

        ShouldBeRefused(export, HttpStatusCode.NotFound, "certification.pharmacy-not-found");
    }

    [Fact]
    public async Task An_employee_with_an_invalid_period_is_forbidden_first()
    {
        Start(manager, MembershipRole.Employee, LongAgo);

        var export = await HandleAsync(new ExportPharmacyCertificates(pharmacy.Value, manager.Value, To, From));

        ShouldBeRefused(export, HttpStatusCode.Forbidden, "certification.capability-missing");
    }

    /// <summary>
    /// Done when (A): a certificate issued before the holder's membership started, or at or after it ended, is not
    /// exported; one issued in between is, also for a former member. Start inclusive, end exclusive.
    /// </summary>
    [Fact]
    public async Task Only_certificates_issued_while_their_holder_was_a_member_are_exported()
    {
        Start(manager, MembershipRole.Manager, LongAgo);
        var former = PersonId.From(Guid.CreateVersion7());
        var started = new DateTimeOffset(2025, 3, 1, 8, 0, 0, TimeSpan.Zero);
        var ended = new DateTimeOffset(2025, 9, 1, 8, 0, 0, TimeSpan.Zero);
        End(Start(former, MembershipRole.Employee, started), former, ended);
        var beforeStart = Issue(former, started.AddTicks(-1));
        var atStart = Issue(former, started);
        var inside = Issue(former, new DateTimeOffset(2025, 5, 10, 9, 0, 0, TimeSpan.Zero));
        var justBeforeEnd = Issue(former, ended.AddTicks(-1));
        var atEnd = Issue(former, ended);
        var afterEnd = Issue(former, new DateTimeOffset(2025, 10, 1, 9, 0, 0, TimeSpan.Zero));

        var exported = await ExportedAsync();

        exported.ShouldBe([atStart.Id, inside.Id, justBeforeEnd.Id]);
        exported.ShouldNotContain(beforeStart.Id);
        exported.ShouldNotContain(atEnd.Id);
        exported.ShouldNotContain(afterEnd.Id);
    }

    [Fact]
    public async Task A_current_members_certificates_from_before_they_joined_are_not_exported()
    {
        Start(manager, MembershipRole.Manager, LongAgo);
        var joiner = PersonId.From(Guid.CreateVersion7());
        Start(joiner, MembershipRole.Employee, new DateTimeOffset(2025, 6, 1, 8, 0, 0, TimeSpan.Zero));
        Issue(joiner, new DateTimeOffset(2025, 5, 31, 9, 0, 0, TimeSpan.Zero));
        var after = Issue(joiner, new DateTimeOffset(2025, 6, 15, 9, 0, 0, TimeSpan.Zero));

        (await ExportedAsync()).ShouldBe([after.Id]);
    }

    [Fact]
    public async Task A_certificate_issued_between_two_memberships_of_its_holder_is_not_exported()
    {
        Start(manager, MembershipRole.Manager, LongAgo);
        var returner = PersonId.From(Guid.CreateVersion7());
        End(Start(returner, MembershipRole.Employee, new DateTimeOffset(2025, 1, 1, 8, 0, 0, TimeSpan.Zero)), returner, new DateTimeOffset(2025, 3, 1, 8, 0, 0, TimeSpan.Zero));
        Start(returner, MembershipRole.Employee, new DateTimeOffset(2025, 6, 1, 8, 0, 0, TimeSpan.Zero));
        var first = Issue(returner, new DateTimeOffset(2025, 2, 1, 9, 0, 0, TimeSpan.Zero));
        Issue(returner, new DateTimeOffset(2025, 4, 1, 9, 0, 0, TimeSpan.Zero));
        var second = Issue(returner, new DateTimeOffset(2025, 7, 1, 9, 0, 0, TimeSpan.Zero));

        (await ExportedAsync()).ShouldBe([first.Id, second.Id]);
    }

    [Fact]
    public async Task The_certificates_of_someone_who_never_worked_in_the_pharmacy_are_not_exported()
    {
        Start(manager, MembershipRole.Manager, LongAgo);
        var elsewhere = PersonId.From(Guid.CreateVersion7());
        Start(elsewhere, MembershipRole.Employee, LongAgo, PharmacyId.From(Guid.CreateVersion7()));
        Issue(elsewhere, new DateTimeOffset(2025, 5, 10, 9, 0, 0, TimeSpan.Zero));
        Issue(PersonId.From(Guid.CreateVersion7()), new DateTimeOffset(2025, 5, 10, 9, 0, 0, TimeSpan.Zero));

        (await ExportedAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task An_ending_whose_start_never_arrived_exports_nothing_of_its_holder()
    {
        Start(manager, MembershipRole.Manager, LongAgo);
        var unknown = PersonId.From(Guid.CreateVersion7());
        End(MembershipId.From(Guid.CreateVersion7()), unknown, new DateTimeOffset(2025, 9, 1, 8, 0, 0, TimeSpan.Zero));
        Issue(unknown, new DateTimeOffset(2025, 5, 10, 9, 0, 0, TimeSpan.Zero));

        (await ExportedAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_certificate_earned_while_its_holder_worked_in_two_pharmacies_is_in_both_exports()
    {
        var other = PharmacyId.From(Guid.CreateVersion7());
        Start(manager, MembershipRole.Manager, LongAgo);
        Start(manager, MembershipRole.Manager, LongAgo, other);
        var both = PersonId.From(Guid.CreateVersion7());
        Start(both, MembershipRole.Employee, LongAgo);
        Start(both, MembershipRole.Employee, LongAgo, other);
        var certificate = Issue(both, new DateTimeOffset(2025, 5, 10, 9, 0, 0, TimeSpan.Zero));

        (await ExportedAsync()).ShouldBe([certificate.Id]);
        (await ExportedAsync(new ExportPharmacyCertificates(other.Value, manager.Value, From, To))).ShouldBe([certificate.Id]);
    }

    [Fact]
    public async Task The_managers_own_certificates_are_exported_too()
    {
        Start(manager, MembershipRole.Manager, LongAgo);
        var own = Issue(manager, new DateTimeOffset(2025, 5, 10, 9, 0, 0, TimeSpan.Zero));

        (await ExportedAsync()).ShouldBe([own.Id]);
    }

    /// <summary>
    /// Done when (B): a certificate whose issue day in Germany lies outside from–to is not exported; both boundary days
    /// are in. 22:00 UTC on 30 April 2025 is already 1 May in German summer time, 21:59:59 still 30 April.
    /// </summary>
    [Fact]
    public async Task Only_certificates_issued_on_a_day_in_germany_from_the_first_through_the_last_of_the_period_are_exported()
    {
        Start(manager, MembershipRole.Manager, LongAgo);
        var member = PersonId.From(Guid.CreateVersion7());
        Start(member, MembershipRole.Employee, LongAgo);
        var dayBefore = Issue(member, new DateTimeOffset(2025, 4, 30, 21, 59, 59, TimeSpan.Zero));
        var firstDay = Issue(member, new DateTimeOffset(2025, 4, 30, 22, 0, 0, TimeSpan.Zero));
        var middle = Issue(member, new DateTimeOffset(2025, 6, 1, 12, 0, 0, TimeSpan.Zero));
        var lastDay = Issue(member, new DateTimeOffset(2025, 6, 30, 21, 59, 59, TimeSpan.Zero));
        var dayAfter = Issue(member, new DateTimeOffset(2025, 6, 30, 22, 0, 0, TimeSpan.Zero));
        var later = Issue(member, new DateTimeOffset(2025, 7, 1, 12, 0, 0, TimeSpan.Zero));

        var exported = await ExportedAsync(new ExportPharmacyCertificates(pharmacy.Value, manager.Value, new DateOnly(2025, 5, 1), new DateOnly(2025, 6, 30)));

        exported.ShouldBe([firstDay.Id, middle.Id, lastDay.Id]);
        exported.ShouldNotContain(dayBefore.Id);
        exported.ShouldNotContain(dayAfter.Id);
        exported.ShouldNotContain(later.Id);
    }

    [Fact]
    public async Task Both_rules_apply_together()
    {
        Start(manager, MembershipRole.Manager, LongAgo);
        var member = PersonId.From(Guid.CreateVersion7());
        Start(member, MembershipRole.Employee, new DateTimeOffset(2025, 5, 15, 8, 0, 0, TimeSpan.Zero));
        Issue(member, new DateTimeOffset(2025, 5, 10, 9, 0, 0, TimeSpan.Zero));
        var both = Issue(member, new DateTimeOffset(2025, 5, 20, 9, 0, 0, TimeSpan.Zero));
        Issue(member, new DateTimeOffset(2025, 7, 20, 9, 0, 0, TimeSpan.Zero));

        var exported = await ExportedAsync(new ExportPharmacyCertificates(pharmacy.Value, manager.Value, new DateOnly(2025, 5, 1), new DateOnly(2025, 6, 30)));

        exported.ShouldBe([both.Id]);
    }

    [Fact]
    public async Task The_certificates_come_oldest_first_whoever_holds_them()
    {
        Start(manager, MembershipRole.Manager, LongAgo);
        var anna = PersonId.From(Guid.CreateVersion7());
        var bea = PersonId.From(Guid.CreateVersion7());
        Start(anna, MembershipRole.Employee, LongAgo);
        Start(bea, MembershipRole.Employee, LongAgo);
        var third = Issue(anna, new DateTimeOffset(2025, 8, 1, 9, 0, 0, TimeSpan.Zero));
        var first = Issue(bea, new DateTimeOffset(2025, 2, 1, 9, 0, 0, TimeSpan.Zero));
        var second = Issue(anna, new DateTimeOffset(2025, 5, 1, 9, 0, 0, TimeSpan.Zero));

        (await ExportedAsync()).ShouldBe([first.Id, second.Id, third.Id]);
    }

    [Fact]
    public async Task A_period_without_certificates_is_an_empty_export()
    {
        Start(manager, MembershipRole.Manager, LongAgo);

        (await ExportedAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task The_archive_is_named_after_the_period()
    {
        Start(manager, MembershipRole.Manager, LongAgo);

        var export = await HandleAsync(manager.Value);

        export.Value.FileName.ShouldBe("teilnahmebestaetigungen_2025-01-01_2025-12-31.zip");
    }

    [Fact]
    public async Task Two_thousand_certificates_may_be_exported()
    {
        Start(manager, MembershipRole.Manager, LongAgo);
        IssueMany(CertificateExportArchive.MaxCertificates);

        (await ExportedAsync()).Count.ShouldBe(2000);
    }

    [Fact]
    public async Task More_than_two_thousand_certificates_are_a_bad_request()
    {
        Start(manager, MembershipRole.Manager, LongAgo);
        IssueMany(CertificateExportArchive.MaxCertificates + 1);

        var export = await HandleAsync(manager.Value);

        ShouldBeRefused(export, HttpStatusCode.BadRequest, "certification.export-too-large");
    }

    [Fact]
    public async Task Certificates_outside_the_membership_do_not_count_towards_the_limit()
    {
        Start(manager, MembershipRole.Manager, LongAgo);
        var member = PersonId.From(Guid.CreateVersion7());
        Start(member, MembershipRole.Employee, new DateTimeOffset(2025, 6, 1, 0, 0, 0, TimeSpan.Zero));
        IssueMany(CertificateExportArchive.MaxCertificates);
        Issue(member, new DateTimeOffset(2025, 5, 1, 9, 0, 0, TimeSpan.Zero));

        (await ExportedAsync()).Count.ShouldBe(2000);
    }

    /// <summary>
    /// #367: the membership rule is applied where the certificates are read, so an export that is refused for holding too
    /// many never loads more than one certificate beyond the limit, however many its members hold from other times.
    /// </summary>
    [Fact]
    public async Task The_export_reads_at_most_one_certificate_more_than_it_may_hold()
    {
        Start(manager, MembershipRole.Manager, LongAgo);
        var joiner = PersonId.From(Guid.CreateVersion7());
        Start(joiner, MembershipRole.Employee, new DateTimeOffset(2025, 6, 1, 8, 0, 0, TimeSpan.Zero));
        var beforeJoining = new DateTimeOffset(2025, 1, 2, 9, 0, 0, TimeSpan.Zero);
        for (var i = 0; i < 3000; i++)
        {
            Issue(joiner, beforeJoining.AddMinutes(i));
        }

        IssueMany(CertificateExportArchive.MaxCertificates + 5);

        var export = await HandleAsync(manager.Value);

        ShouldBeRefused(export, HttpStatusCode.BadRequest, "certification.export-too-large");
        certificates.CertificatesReadForExports.ShouldBeLessThanOrEqualTo(CertificateExportArchive.MaxCertificates + 1);
    }

    /// <summary>
    /// #53 (docs/adr/0034): the pharmacy keeps a certificate in its view until five years after its issue, to the instant,
    /// whatever period is asked for. Issued 15 June 2025 at 10:00 UTC, it is exported on 14 June 2030 (4 years 364 days)
    /// and no longer at 10:00 UTC on 15 June 2030 (exactly 5 years).
    /// </summary>
    [Fact]
    public async Task A_certificate_is_exported_until_five_years_after_its_issue()
    {
        Start(manager, MembershipRole.Manager, LongAgo);
        var member = PersonId.From(Guid.CreateVersion7());
        Start(member, MembershipRole.Employee, LongAgo);
        var issuedAt = new DateTimeOffset(2025, 6, 15, 10, 0, 0, TimeSpan.Zero);
        var certificate = Issue(member, issuedAt);

        time.SetUtcNow(issuedAt.AddYears(5).AddDays(-1));
        (await ExportedAsync()).ShouldBe([certificate.Id]);

        time.SetUtcNow(issuedAt.AddYears(5).AddTicks(-1));
        (await ExportedAsync()).ShouldBe([certificate.Id]);

        time.SetUtcNow(issuedAt.AddYears(5));
        (await ExportedAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Of_one_period_only_the_certificates_issued_less_than_five_years_ago_are_exported()
    {
        Start(manager, MembershipRole.Manager, LongAgo);
        var member = PersonId.From(Guid.CreateVersion7());
        Start(member, MembershipRole.Employee, LongAgo);
        time.SetUtcNow(new DateTimeOffset(2030, 6, 15, 10, 0, 0, TimeSpan.Zero));
        Issue(member, new DateTimeOffset(2025, 2, 1, 9, 0, 0, TimeSpan.Zero));
        Issue(member, new DateTimeOffset(2025, 6, 15, 10, 0, 0, TimeSpan.Zero));
        var dayLater = Issue(member, new DateTimeOffset(2025, 6, 16, 10, 0, 0, TimeSpan.Zero));
        var later = Issue(member, new DateTimeOffset(2025, 11, 1, 9, 0, 0, TimeSpan.Zero));

        (await ExportedAsync()).ShouldBe([dayLater.Id, later.Id]);
    }

    /// <summary>
    /// #53: certificates the pharmacy no longer keeps are left out before the 2000 are counted, so an export that holds
    /// 2000 kept certificates is not refused for older ones in the same period.
    /// </summary>
    [Fact]
    public async Task Certificates_older_than_five_years_do_not_count_towards_the_limit()
    {
        Start(manager, MembershipRole.Manager, LongAgo);
        time.SetUtcNow(new DateTimeOffset(2030, 6, 15, 10, 0, 0, TimeSpan.Zero));
        IssueMany(CertificateExportArchive.MaxCertificates, new DateTimeOffset(2025, 7, 1, 9, 0, 0, TimeSpan.Zero));
        IssueMany(5, new DateTimeOffset(2025, 3, 1, 9, 0, 0, TimeSpan.Zero));

        (await ExportedAsync()).Count.ShouldBe(2000);
    }

    [Fact]
    public async Task No_query_is_refused()
    {
        await Should.ThrowAsync<GuardException>(() => Handler().HandleAsync(null!, TestContext.Current.CancellationToken));
    }

    private static DateOnly? DateOf(string? text) =>
        text is null ? null : DateOnly.ParseExact(text, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    private ExportPharmacyCertificatesHandler Handler() => new(memberships, certificates, blobs, time);

    private Task<Result<CertificateExportArchive, ApiError>> HandleAsync(Guid caller) =>
        HandleAsync(new ExportPharmacyCertificates(pharmacy.Value, caller, From, To));

    private Task<Result<CertificateExportArchive, ApiError>> HandleAsync(ExportPharmacyCertificates query) =>
        Handler().HandleAsync(query, TestContext.Current.CancellationToken);

    private Task<List<CertificateId>> ExportedAsync() =>
        ExportedAsync(new ExportPharmacyCertificates(pharmacy.Value, manager.Value, From, To));

    private async Task<List<CertificateId>> ExportedAsync(ExportPharmacyCertificates query)
    {
        var export = await HandleAsync(query);
        export.IsSuccess.ShouldBeTrue(export.IsFailure ? export.Error.Code : string.Empty);
        blobs.Reads.ShouldBeEmpty("the handler only chooses the certificates; the archive reads their PDFs as it writes them");
        return export.Value.Certificates.Select(certificate => certificate.Id).ToList();
    }

    private MembershipId Start(PersonId person, MembershipRole role, DateTimeOffset startedAt, PharmacyId? where = null)
    {
        var id = MembershipId.From(Guid.CreateVersion7());
        memberships.Memberships.Add(Membership.Started(id, person, where ?? pharmacy, HolderName.From("Anna Berger"), role, Timestamp.From(startedAt)));
        return id;
    }

    private void End(MembershipId membership, PersonId person, DateTimeOffset endedAt) =>
        memberships.Endings.Add(MembershipEnding.Of(membership, person, pharmacy, Timestamp.From(endedAt)));

    private void Change(MembershipId membership, PersonId person, int version, MembershipRole role) =>
        memberships.Changes.Add(MembershipChange.Of(membership, person, pharmacy, MembershipVersion.From(version), role));

    private Certificate Issue(PersonId holder, DateTimeOffset issuedAt)
    {
        var certificate = ExportCertificates.Stored(holder, issuedAt);
        certificates.Stored.Add(certificate);
        return certificate;
    }

    private void IssueMany(int count) => IssueMany(count, new DateTimeOffset(2025, 1, 1, 9, 0, 0, TimeSpan.Zero));

    private void IssueMany(int count, DateTimeOffset first)
    {
        var member = PersonId.From(Guid.CreateVersion7());
        Start(member, MembershipRole.Employee, LongAgo);
        for (var i = 0; i < count; i++)
        {
            Issue(member, first.AddMinutes(i));
        }
    }

    private void ShouldBeRefused(Result<CertificateExportArchive, ApiError> export, HttpStatusCode status, string code)
    {
        export.IsFailure.ShouldBeTrue(export.IsSuccess ? $"exported {export.Value.Certificates.Count} certificates" : string.Empty);
        export.Error.Code.ShouldBe(code);
        export.Error.Status.ShouldBe(status);
        blobs.Reads.ShouldBeEmpty("nothing is read from blob storage for a refused caller");
    }
}
