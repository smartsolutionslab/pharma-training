using System.Globalization;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Assignments;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Tests.Assignments;

/// <summary>The values an assignment is made of: its identities, instants, days, due date, recurrence and the reason for a change.</summary>
public class AssignmentValueObjectTests
{
    [Fact]
    public void An_empty_guid_is_no_assignment_id()
    {
        Should.Throw<GuardException>(() => AssignmentId.From(Guid.Empty));
    }

    [Fact]
    public void An_empty_guid_is_no_certificate_id()
    {
        Should.Throw<GuardException>(() => CertificateId.From(Guid.Empty));
    }

    [Fact]
    public void A_new_assignment_id_is_a_version_7_guid()
    {
        var id = AssignmentId.New();

        id.Value.Version.ShouldBe(7);
        id.ShouldBe(AssignmentId.From(id.Value));
        id.ShouldNotBe(AssignmentId.New());
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
        RecurrenceInterval.Yearly.Value.ShouldBe(12);
        RecurrenceInterval.Yearly.ShouldBe(RecurrenceInterval.From(12));
    }

    [Fact]
    public void Plus_adds_the_recurrence_in_months()
    {
        var day = CalendarDate.From(new DateOnly(2027, 1, 31));

        day.Plus(RecurrenceInterval.From(1)).ShouldBe(CalendarDate.From(new DateOnly(2027, 2, 28)));
    }

    [Fact]
    public void A_due_date_is_the_day_it_falls_on()
    {
        var day = CalendarDate.From(new DateOnly(2027, 3, 31));

        DueDate.On(day).ShouldBe(DueDate.From(new DateOnly(2027, 3, 31)));
        DueDate.On(day).AsDate().ShouldBe(day);
    }

    [Theory]
    [InlineData(null, 1)]
    [InlineData("", 1)]
    [InlineData("   ", 1)]
    [InlineData("x", 501)]
    public void A_blank_or_too_long_change_reason_is_refused(string? reason, int repeat)
    {
        var value = reason is null ? null : string.Concat(Enumerable.Repeat(reason, repeat));

        Should.Throw<GuardException>(() => ChangeReason.From(value!));
    }

    /// <summary>A reason is free text kept in the assignment's jsonb history: PostgreSQL refuses U+0000 there (22P05).</summary>
    [Theory]
    [InlineData("Urlaub\u0000")]
    [InlineData("Ur\u0007laub")]
    [InlineData("Ur\u200Blaub")]
    [InlineData("Ur\u202Elaub")]
    public void A_change_reason_with_a_control_or_format_character_is_refused(string reason)
    {
        Should.Throw<GuardException>(() => ChangeReason.From(reason));
        ChangeReason.TryFrom(reason, out var refused).ShouldBeFalse();
        refused.ShouldBeNull();
    }

    [Fact]
    public void A_change_reason_may_hold_line_breaks_and_tabs()
    {
        ChangeReason.From("Urlaub\r\nbis\tMai").Value.ShouldBe("Urlaub\r\nbis\tMai");
        ChangeReason.TryFrom("Urlaub", out var reason).ShouldBeTrue();
        reason.ShouldBe(ChangeReason.From("Urlaub"));
        ChangeReason.TryFrom(null, out var none).ShouldBeFalse();
        none.ShouldBeNull();
    }

    [Fact]
    public void A_change_reason_of_the_longest_length_is_kept()
    {
        var value = new string('x', ChangeReason.MaxLength);

        ChangeReason.From(value).Value.ShouldBe(value);
    }

    [Fact]
    public void A_withdrawal_by_the_manager_has_its_own_reason()
    {
        ChangeReason.Withdrawn.Value.ShouldBe("Von der Leitung zurückgezogen");
        ChangeReason.Withdrawn.ShouldBe(ChangeReason.From("Von der Leitung zurückgezogen"));
        ChangeReason.Withdrawn.ShouldNotBe(ChangeReason.MembershipEnded);
    }
}
