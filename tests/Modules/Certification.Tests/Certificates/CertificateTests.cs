using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Certificates;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Certificates.Events;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Retention;

// Certification — behaviour of the Certificate aggregate.

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Tests.Certificates;

public class CertificateTests
{
    private static readonly Timestamp PassedAt = Timestamp.From(new DateTimeOffset(2027, 3, 1, 23, 30, 0, TimeSpan.Zero));

    [Fact]
    public void Issuing_announces_the_certificate_with_its_validity()
    {
        var certificate = Issue(RecurrenceInterval.Yearly);

        certificate.DomainEvents.OfType<CertificateIssued>().ShouldHaveSingleItem().ValidUntil.ShouldBe(certificate.ValidUntil);
        certificate.Document.ShouldBe(DocumentState.Pending);
    }

    [Fact]
    public void Validity_counts_from_the_german_calendar_day_of_passing()
    {
        // 23:30 UTC on 1 March is already 2 March in Germany.
        var certificate = Issue(RecurrenceInterval.Yearly);

        certificate.ValidUntil.ShouldBe(CalendarDate.From(new DateOnly(2028, 3, 2)));
    }

    [Fact]
    public void First_aid_is_valid_for_two_years()
    {
        Issue(RecurrenceInterval.From(24)).ValidUntil.ShouldBe(CalendarDate.From(new DateOnly(2029, 3, 2)));
    }

    [Theory]
    [InlineData(2028, 3, 2, Validity.Valid)]
    [InlineData(2028, 3, 3, Validity.Expired)]
    public void A_certificate_is_valid_through_its_last_day(int year, int month, int day, Validity expected)
    {
        Issue(RecurrenceInterval.Yearly).ValidityOn(CalendarDate.From(new DateOnly(year, month, day))).ShouldBe(expected);
    }

    [Fact]
    public void The_document_is_stored_once_and_never_replaced()
    {
        var certificate = Issue(RecurrenceInterval.Yearly);
        var first = DocumentReference.From("certificates/2027/03/a.pdf");

        certificate.AttachDocument(first);
        certificate.AttachDocument(DocumentReference.From("certificates/2027/03/b.pdf")); // a retried render

        certificate.Document.ShouldBe(DocumentState.Stored);
        certificate.DocumentLocation.ShouldBe(first);
    }

    [Fact]
    public void The_certificate_keeps_the_course_versions_number_beside_its_title()
    {
        var certificate = Issue(RecurrenceInterval.Yearly);

        certificate.CourseTitle.ShouldBe(CourseTitle.From("Hygiene 2027"));
        certificate.CourseVersionNumber.ShouldBe(CourseVersionNumber.From(3));
    }

    /// <summary>
    /// #53 (docs/adr/0034): a pharmacy sees a certificate until five years after it was issued, to the instant; at
    /// IssuedAt + 5 years it is gone from the pharmacy's view. The holder keeps it.
    /// </summary>
    [Theory]
    [InlineData("2032-02-28T23:30:00Z", true)] // 4 years 364 days
    [InlineData("2032-02-29T23:30:00Z", true)] // 4 years 365 days, a day before (2032 is a leap year)
    [InlineData("2032-03-01T23:29:59.9999999Z", true)]
    [InlineData("2032-03-01T23:30:00Z", false)] // exactly 5 years
    [InlineData("2040-01-01T00:00:00Z", false)]
    public void A_pharmacy_keeps_a_certificate_until_five_years_after_its_issue(string now, bool kept)
    {
        var at = Timestamp.From(DateTimeOffset.Parse(now, System.Globalization.CultureInfo.InvariantCulture));

        Issue(RecurrenceInterval.Yearly).IsKeptByPharmaciesAt(at, RetentionPeriod.Default).ShouldBe(kept);
    }

    /// <summary>
    /// #53 (docs/adr/0034): the five years are counted on the UTC calendar, as <see cref="DateTimeOffset.AddYears"/>
    /// counts them, which the export's query repeats in SQL. 29 February ends on 28 February five years on, at its UTC
    /// time of day, so a certificate issued late on 28 February outlasts one issued early on 29 February. One issued at
    /// 23:30 UTC on 29 February (00:30 on 1 March in Germany) is kept by its UTC date, until 23:30 UTC on 28 February.
    /// </summary>
    [Theory]
    [InlineData("2028-02-29T01:00:00Z", "2033-02-28T00:59:59.9999999Z", true)]
    [InlineData("2028-02-29T01:00:00Z", "2033-02-28T01:00:00Z", false)]
    [InlineData("2028-02-28T13:00:00Z", "2033-02-28T12:00:00Z", true)] // issued earlier, kept longer
    [InlineData("2028-02-29T01:00:00Z", "2033-02-28T12:00:00Z", false)]
    [InlineData("2028-02-29T23:30:00Z", "2033-02-28T23:29:59.9999999Z", true)] // 1 March 00:30 in Germany
    [InlineData("2028-02-29T23:30:00Z", "2033-02-28T23:30:00Z", false)]
    [InlineData("2027-02-28T23:30:00Z", "2032-02-29T00:00:00Z", false)] // on 29 February, 28 February is past
    [InlineData("2027-03-01T00:30:00Z", "2032-02-29T23:59:59Z", true)]
    public void Five_years_around_29_February_are_counted_on_the_UTC_calendar(string issuedAt, string now, bool kept)
    {
        var certificate = Issue(RecurrenceInterval.Yearly, Timestamp.From(DateTimeOffset.Parse(issuedAt, System.Globalization.CultureInfo.InvariantCulture)));

        certificate.IsKeptByPharmaciesAt(Timestamp.From(DateTimeOffset.Parse(now, System.Globalization.CultureInfo.InvariantCulture)), RetentionPeriod.Default).ShouldBe(kept);
    }

    /// <summary>
    /// #391: a pharmacy keeps a certificate for its own period, to the instant, as it keeps it for the default five years.
    /// Issued at 23:30 UTC on 1 March 2027.
    /// </summary>
    [Theory]
    [InlineData(7, "2034-03-01T23:29:59.9999999Z", true)]
    [InlineData(7, "2034-03-01T23:30:00Z", false)]
    [InlineData(2, "2029-03-01T23:29:59.9999999Z", true)]
    [InlineData(2, "2029-03-01T23:30:00Z", false)]
    [InlineData(30, "2057-03-01T23:29:59.9999999Z", true)]
    [InlineData(30, "2057-03-01T23:30:00Z", false)]
    public void A_pharmacy_keeps_a_certificate_for_its_own_period_to_the_instant(int years, string now, bool kept)
    {
        Issue(RecurrenceInterval.Yearly).IsKeptByPharmaciesAt(At(now), RetentionPeriod.From(years)).ShouldBe(kept);
    }

    /// <summary>#391: the period applies to every certificate, whenever it was chosen: one six years old is kept at seven years, not at five.</summary>
    [Fact]
    public void A_certificate_six_years_old_is_kept_at_seven_years_and_not_at_five()
    {
        var certificate = Issue(RecurrenceInterval.Yearly);
        var sixYearsLater = Timestamp.From(PassedAt.Value.AddYears(6));

        certificate.IsKeptByPharmaciesAt(sixYearsLater, RetentionPeriod.From(7)).ShouldBeTrue();
        certificate.IsKeptByPharmaciesAt(sixYearsLater, RetentionPeriod.Default).ShouldBeFalse();
    }

    /// <summary>
    /// #391: a period counts 29 February as <see cref="DateTimeOffset.AddYears"/> does, which PostgreSQL's
    /// <c>make_interval</c> repeats: four years on it is 29 February again, seven years on it is 28 February.
    /// </summary>
    [Theory]
    [InlineData(4, "2032-02-29T00:59:59.9999999Z", true)]
    [InlineData(4, "2032-02-29T01:00:00Z", false)]
    [InlineData(7, "2035-02-28T00:59:59.9999999Z", true)]
    [InlineData(7, "2035-02-28T01:00:00Z", false)]
    public void A_period_counts_29_February_as_AddYears_does(int years, string now, bool kept)
    {
        var certificate = Issue(RecurrenceInterval.Yearly, At("2028-02-29T01:00:00Z"));

        certificate.IsKeptByPharmaciesAt(At(now), RetentionPeriod.From(years)).ShouldBe(kept);
    }

    [Fact]
    public void A_verification_code_is_never_short()
    {
        Should.Throw<GuardException>(() => VerificationCode.From("abc"));
    }

    private static Timestamp At(string instant) =>
        Timestamp.From(DateTimeOffset.Parse(instant, System.Globalization.CultureInfo.InvariantCulture));

    private static Certificate Issue(RecurrenceInterval validity) => Issue(validity, PassedAt);

    private static Certificate Issue(RecurrenceInterval validity, Timestamp passedAt) =>
        Certificate.Issue(
            CertificateId.From(Guid.CreateVersion7()),
            AttemptId.From(Guid.CreateVersion7()),
            PersonId.From(Guid.CreateVersion7()),
            HolderName.From("Anna Berger"),
            CourseId.From(Guid.CreateVersion7()),
            CourseVersionId.From(Guid.CreateVersion7()),
            CourseTitle.From("Hygiene 2027"),
            CourseVersionNumber.From(3),
            validity,
            ScorePercent.From(90),
            VerificationCode.From("k7Qp2xV9mL4rT8wZ1nB6cY3h"),
            passedAt);
}
