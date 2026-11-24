using System.Globalization;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Certificates;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Tests.Certificates;

/// <summary>
/// The values a certificate is made of: its identities, the holder's name, the course title, the verification code,
/// where its document lies, the score, the validity in months, the instants and the days.
/// </summary>
public class CertificateValueObjectTests
{
    [Fact]
    public void An_empty_guid_is_no_certificate_id()
    {
        Should.Throw<GuardException>(() => CertificateId.From(Guid.Empty));
    }

    [Fact]
    public void An_empty_guid_is_no_attempt_id()
    {
        Should.Throw<GuardException>(() => AttemptId.From(Guid.Empty));
    }

    [Fact]
    public void An_empty_guid_is_no_person_id()
    {
        Should.Throw<GuardException>(() => PersonId.From(Guid.Empty));
    }

    [Fact]
    public void An_empty_guid_is_no_course_id()
    {
        Should.Throw<GuardException>(() => CourseId.From(Guid.Empty));
    }

    [Fact]
    public void An_empty_guid_is_no_course_version_id()
    {
        Should.Throw<GuardException>(() => CourseVersionId.From(Guid.Empty));
    }

    [Fact]
    public void A_new_certificate_id_is_a_version_7_guid()
    {
        var id = CertificateId.New();

        id.Value.Version.ShouldBe(7);
        id.ShouldBe(CertificateId.From(id.Value));
        id.ShouldNotBe(CertificateId.New());
    }

    [Fact]
    public void Each_identity_comes_back_from_its_value()
    {
        var value = Guid.CreateVersion7();

        CertificateId.From(value).Value.ShouldBe(value);
        AttemptId.From(value).Value.ShouldBe(value);
        PersonId.From(value).Value.ShouldBe(value);
        CourseId.From(value).Value.ShouldBe(value);
        CourseVersionId.From(value).Value.ShouldBe(value);
        CertificateId.From(value).ToString().ShouldBe(value.ToString());
    }

    [Theory]
    [InlineData(null, 1)]
    [InlineData("", 1)]
    [InlineData("   ", 1)]
    [InlineData("a", 101)]
    public void A_blank_or_too_long_holder_name_is_refused(string? name, int repeat)
    {
        Should.Throw<GuardException>(() => HolderName.From(Repeated(name, repeat)!));
    }

    [Fact]
    public void A_holder_name_of_the_longest_length_is_kept_and_a_name_is_trimmed()
    {
        var longest = new string('a', HolderName.MaxLength);

        HolderName.MaxLength.ShouldBe(100);
        HolderName.From(longest).Value.ShouldBe(longest);
        HolderName.From("  Anna Berger ").Value.ShouldBe("Anna Berger");
    }

    [Theory]
    [InlineData(null, 1)]
    [InlineData("", 1)]
    [InlineData("   ", 1)]
    [InlineData("a", 151)]
    public void A_blank_or_too_long_course_title_is_refused(string? title, int repeat)
    {
        Should.Throw<GuardException>(() => CourseTitle.From(Repeated(title, repeat)!));
    }

    [Fact]
    public void A_course_title_of_the_longest_length_is_kept_and_a_title_is_trimmed()
    {
        var longest = new string('a', CourseTitle.MaxLength);

        CourseTitle.MaxLength.ShouldBe(150);
        CourseTitle.From(longest).Value.ShouldBe(longest);
        CourseTitle.From(" Hygiene 2027  ").Value.ShouldBe("Hygiene 2027");
    }

    [Theory]
    [InlineData(23)]
    [InlineData(65)]
    public void A_verification_code_shorter_than_24_or_longer_than_64_characters_is_refused(int length)
    {
        Should.Throw<GuardException>(() => VerificationCode.From(new string('k', length)));
    }

    [Theory]
    [InlineData(24)]
    [InlineData(64)]
    public void A_verification_code_of_24_to_64_characters_is_kept(int length)
    {
        var code = new string('k', length);

        VerificationCode.From(code).Value.ShouldBe(code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("                        ")]
    public void A_missing_or_blank_verification_code_is_refused(string? code)
    {
        Should.Throw<GuardException>(() => VerificationCode.From(code!));
    }

    [Theory]
    [InlineData(null, 1)]
    [InlineData("", 1)]
    [InlineData("   ", 1)]
    [InlineData("a", 1025)]
    public void A_blank_or_too_long_document_reference_is_refused(string? location, int repeat)
    {
        Should.Throw<GuardException>(() => DocumentReference.From(Repeated(location, repeat)!));
    }

    [Fact]
    public void A_document_reference_of_the_longest_length_is_kept()
    {
        var longest = new string('a', DocumentReference.MaxLength);

        DocumentReference.MaxLength.ShouldBe(1024);
        DocumentReference.From(longest).Value.ShouldBe(longest);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void A_score_outside_zero_to_one_hundred_percent_is_refused(int score)
    {
        Should.Throw<GuardException>(() => ScorePercent.From(score));
        ScorePercent.TryFrom(score, out _).ShouldBeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    public void A_score_of_zero_to_one_hundred_percent_is_kept(int score)
    {
        ScorePercent.From(score).Value.ShouldBe(score);
        ScorePercent.TryFrom(score, out var result).ShouldBeTrue();
        result.ShouldBe(ScorePercent.From(score));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(61)]
    public void A_recurrence_outside_one_to_sixty_months_is_refused(int months)
    {
        Should.Throw<GuardException>(() => RecurrenceInterval.From(months));
        RecurrenceInterval.TryFrom(months, out _).ShouldBeFalse();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(60)]
    public void A_recurrence_of_one_to_sixty_months_is_accepted(int months)
    {
        RecurrenceInterval.From(months).Value.ShouldBe(months);
        RecurrenceInterval.TryFrom(months, out var interval).ShouldBeTrue();
        interval.ShouldBe(RecurrenceInterval.From(months));
    }

    [Fact]
    public void A_yearly_recurrence_is_twelve_months()
    {
        RecurrenceInterval.Yearly.ShouldBe(RecurrenceInterval.From(12));
    }

    [Fact]
    public void A_timestamp_is_kept_in_utc()
    {
        var timestamp = Timestamp.From(new DateTimeOffset(2027, 3, 1, 9, 0, 0, TimeSpan.FromHours(1)));

        timestamp.Value.Offset.ShouldBe(TimeSpan.Zero);
        timestamp.Value.ShouldBe(new DateTimeOffset(2027, 3, 1, 8, 0, 0, TimeSpan.Zero));
    }

    [Theory]
    [InlineData("2027-03-01T23:30:00Z", "2027-03-02")]
    [InlineData("2027-07-01T21:59:00Z", "2027-07-01")]
    [InlineData("2027-07-01T22:00:00Z", "2027-07-02")]
    public void The_day_in_germany_follows_german_local_time(string instant, string day)
    {
        var timestamp = Timestamp.From(DateTimeOffset.Parse(instant, CultureInfo.InvariantCulture));

        timestamp.DayInGermany().ShouldBe(CalendarDate.From(DateOnly.Parse(day, CultureInfo.InvariantCulture)));
    }

    [Fact]
    public void Timestamps_compare_by_their_instant()
    {
        var earlier = Timestamp.From(new DateTimeOffset(2027, 3, 1, 9, 0, 0, TimeSpan.Zero));
        var later = Timestamp.From(new DateTimeOffset(2027, 3, 1, 9, 0, 1, TimeSpan.Zero));
        var sameAsEarlier = Timestamp.From(new DateTimeOffset(2027, 3, 1, 10, 0, 0, TimeSpan.FromHours(1)));

        (earlier < later).ShouldBeTrue();
        (later > earlier).ShouldBeTrue();
        (earlier <= sameAsEarlier).ShouldBeTrue();
        (earlier >= sameAsEarlier).ShouldBeTrue();
        (later <= earlier).ShouldBeFalse();
        earlier.CompareTo(null).ShouldBe(1);
        earlier.ShouldBe(sameAsEarlier);
    }

    [Fact]
    public void Calendar_dates_compare_by_their_day()
    {
        var first = CalendarDate.From(new DateOnly(2027, 3, 1));
        var second = CalendarDate.From(new DateOnly(2027, 3, 2));

        (first < second).ShouldBeTrue();
        (second > first).ShouldBeTrue();
        (first <= CalendarDate.From(new DateOnly(2027, 3, 1))).ShouldBeTrue();
        (second >= first).ShouldBeTrue();
        (second < first).ShouldBeFalse();
        first.CompareTo(null).ShouldBe(1);
    }

    [Theory]
    [InlineData("2027-01-31", 1, "2027-02-28")]
    [InlineData("2028-01-31", 1, "2028-02-29")]
    [InlineData("2028-02-29", 12, "2029-02-28")]
    [InlineData("2027-03-02", 24, "2029-03-02")]
    public void Plus_adds_the_months_and_a_day_the_shorter_month_lacks_becomes_its_last(string day, int months, string expected)
    {
        var date = CalendarDate.From(DateOnly.Parse(day, CultureInfo.InvariantCulture));

        date.Plus(RecurrenceInterval.From(months)).ShouldBe(CalendarDate.From(DateOnly.Parse(expected, CultureInfo.InvariantCulture)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_course_version_number_below_one_is_refused(int number)
    {
        Should.Throw<GuardException>(() => CourseVersionNumber.From(number));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(37)]
    public void A_course_version_number_from_one_comes_back_from_its_value(int number)
    {
        CourseVersionNumber.From(number).Value.ShouldBe(number);
        CourseVersionNumber.From(number).ShouldBe(CourseVersionNumber.From(number));
        CourseVersionNumber.Minimum.ShouldBe(1);
    }

    private static string? Repeated(string? value, int repeat) =>
        value is null ? null : string.Concat(Enumerable.Repeat(value, repeat));
}
