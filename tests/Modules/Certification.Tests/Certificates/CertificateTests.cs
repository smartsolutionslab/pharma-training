using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Certificates;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Certificates.Events;

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

        Issue(RecurrenceInterval.Yearly).IsKeptByPharmaciesAt(at).ShouldBe(kept);
    }

    [Fact]
    public void A_verification_code_is_never_short()
    {
        Should.Throw<GuardException>(() => VerificationCode.From("abc"));
    }

    private static Certificate Issue(RecurrenceInterval validity) =>
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
            PassedAt);
}
