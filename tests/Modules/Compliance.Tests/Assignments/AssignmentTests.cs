using System.Globalization;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Assignments;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Assignments.Events;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Courses;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Members;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Tests.Assignments;

/// <summary>
/// Behaviour of the Assignment aggregate: the reference tests (docs/domain/reference/tests/AssignmentTests.cs) with only
/// what the error union forces changed, the helper's return type and <c>.Error.Value</c> where a step returns the union.
/// </summary>

public class AssignmentTests
{
    private static readonly Timestamp Monday = Timestamp.From(new DateTimeOffset(2027, 3, 1, 9, 0, 0, TimeSpan.FromHours(1)));
    private static readonly PersonId Anna = PersonId.From(Guid.CreateVersion7());
    private static readonly PersonId Manager = PersonId.From(Guid.CreateVersion7());
    private static readonly PharmacyId Pharmacy = PharmacyId.From(Guid.CreateVersion7());
    private static readonly CourseId Hygiene = CourseId.From(Guid.CreateVersion7());

    [Fact]
    public void An_assignment_starts_open_and_announces_itself()
    {
        var assignment = Assign(DueInDays(30)).Value;

        assignment.Status.ShouldBe(AssignmentStatus.Open);
        assignment.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<CourseAssigned>();
    }

    [Fact]
    public void A_due_date_in_the_past_is_refused()
    {
        Assign(DueInDays(-1)).Error.Value.ShouldBeOfType<DueDateInThePast>();
    }

    [Fact]
    public void A_due_date_of_today_is_accepted()
    {
        Assign(DueInDays(0)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Opening_the_course_puts_the_assignment_in_progress_once()
    {
        var assignment = Assign(DueInDays(30)).Value;

        assignment.MarkStarted(Monday);
        assignment.MarkStarted(Later(2)); // redelivered event

        assignment.Status.ShouldBe(AssignmentStatus.InProgress);
        assignment.StartedAt.ShouldBe(Monday);
    }

    [Fact]
    public void A_certificate_completes_an_assignment_that_never_saw_the_start()
    {
        var assignment = Assign(DueInDays(30)).Value;

        assignment.Complete(Certificate(), Later(1)).IsSuccess.ShouldBeTrue(); // CertificateIssued overtook LearningSessionStarted

        assignment.Status.ShouldBe(AssignmentStatus.Completed);
    }

    [Fact]
    public void The_same_certificate_twice_changes_nothing()
    {
        var assignment = Assign(DueInDays(30)).Value;
        var certificate = Certificate();
        assignment.Complete(certificate, Later(1));
        assignment.ClearDomainEvents();

        assignment.Complete(certificate, Later(1)).IsSuccess.ShouldBeTrue();

        assignment.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void A_certificate_from_before_the_assignment_does_not_fulfil_it()
    {
        var assignment = Assign(DueInDays(30)).Value;

        assignment.Complete(Certificate(), Later(-1)).Error.Value.ShouldBeOfType<CertificatePredatesAssignment>();
    }

    [Fact]
    public void A_completed_assignment_cannot_be_cancelled()
    {
        var assignment = Assign(DueInDays(30)).Value;
        assignment.Complete(Certificate(), Later(1));

        assignment.Cancel(Manager, ChangeReason.From("Versehen"), Later(2)).Error.Value.ShouldBeOfType<AssignmentNotActive>();
    }

    [Fact]
    public void Cancelling_twice_is_harmless_because_membership_ended_may_arrive_twice()
    {
        var assignment = Assign(DueInDays(30)).Value;
        assignment.Cancel(Manager, ChangeReason.MembershipEnded, Later(1));

        assignment.Cancel(Manager, ChangeReason.MembershipEnded, Later(1)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Moving_the_due_date_leaves_a_trace_in_the_history()
    {
        var assignment = Assign(DueInDays(30)).Value;

        assignment.ChangeDueDate(DueInDays(60), Manager, ChangeReason.From("Urlaub"), Later(1)).IsSuccess.ShouldBeTrue();

        var change = assignment.History.ShouldHaveSingleItem();
        change.Kind.ShouldBe(AssignmentChangeKind.DueDateChanged);
        change.PreviousDueDate.ShouldBe(DueInDays(30));
        change.NewDueDate.ShouldBe(DueInDays(60));
        change.ChangedBy.ShouldBe(Manager);
    }

    [Theory]
    [InlineData(29, AssignmentState.Open)]
    [InlineData(30, AssignmentState.Open)]
    [InlineData(31, AssignmentState.Overdue)]
    public void Overdue_starts_the_day_after_the_due_date(int daysFromMonday, AssignmentState expected)
    {
        var assignment = Assign(DueInDays(30)).Value;

        assignment.StateOn(Day(daysFromMonday)).ShouldBe(expected);
    }

    [Fact]
    public void Three_failed_attempts_show_as_locked_until_the_learner_relearns_the_content()
    {
        var assignment = Assign(DueInDays(30)).Value;
        assignment.MarkStarted(Monday);

        assignment.MarkLocked(Later(1));
        assignment.StateOn(Day(1)).ShouldBe(AssignmentState.Locked);

        assignment.MarkStarted(Later(2));
        assignment.StateOn(Day(2)).ShouldBe(AssignmentState.Locked);

        assignment.Unlock(Later(3));
        assignment.StateOn(Day(3)).ShouldBe(AssignmentState.InProgress);
    }

    [Fact]
    public void Completing_after_the_due_date_shows_as_completed_late()
    {
        var assignment = Assign(DueInDays(30)).Value;

        assignment.Complete(Certificate(), Later(35));

        assignment.StateOn(Day(40)).ShouldBe(AssignmentState.CompletedLate);
    }

    [Fact]
    public void A_yearly_assignment_follows_up_due_one_year_after_completion()
    {
        var assignment = Assign(DueInDays(30), RecurrenceInterval.Yearly).Value;
        assignment.Complete(Certificate(), Later(10));

        var next = assignment.FollowUp(AssignmentId.New(), Later(10)).Value;

        next.DueDate.ShouldBe(DueDate.On(Day(10).Plus(RecurrenceInterval.Yearly)));
        next.Status.ShouldBe(AssignmentStatus.Open);
    }

    [Fact]
    public void A_one_off_assignment_has_no_follow_up()
    {
        var assignment = Assign(DueInDays(30)).Value;
        assignment.Complete(Certificate(), Later(10));

        assignment.FollowUp(AssignmentId.New(), Later(10)).HasValue.ShouldBeFalse();
    }

    [Fact]
    public void A_follow_up_records_the_assignment_it_follows()
    {
        var assignment = Assign(DueInDays(30), RecurrenceInterval.Yearly).Value;
        assignment.Complete(Certificate(), Later(10));

        var next = assignment.FollowUp(AssignmentId.New(), Later(10)).Value;

        next.Follows.ShouldBe(assignment.Id);
        assignment.Follows.ShouldBeNull();
    }

    [Fact]
    public void A_follow_up_is_never_assigned_before_the_completion_it_follows()
    {
        var assignment = Assign(DueInDays(30), RecurrenceInterval.Yearly).Value;
        assignment.Complete(Certificate(), Later(10));

        // Compliance's clock behind Certification's: the follow-up is taken a day before the certificate was issued.
        var next = assignment.FollowUp(AssignmentId.New(), Later(9)).Value;

        next.AssignedAt.ShouldBe(Later(10));
    }

    [Fact]
    public void The_certificate_that_completed_an_assignment_does_not_complete_its_follow_up()
    {
        var assignment = Assign(DueInDays(30), RecurrenceInterval.Yearly).Value;
        var certificate = Certificate();
        assignment.Complete(certificate, Later(10));
        var next = assignment.FollowUp(AssignmentId.New(), Later(10)).Value;

        var completed = next.Complete(certificate, Later(10));

        completed.IsFailure.ShouldBeTrue();
        completed.Error.Value.ShouldBeOfType<CertificatePredatesAssignment>();
        next.Status.ShouldBe(AssignmentStatus.Open);
        next.Certificate.ShouldBeNull();
    }

    [Fact]
    public void A_later_certificate_completes_the_follow_up()
    {
        var assignment = Assign(DueInDays(30), RecurrenceInterval.Yearly).Value;
        assignment.Complete(Certificate(), Later(10));
        var next = assignment.FollowUp(AssignmentId.New(), Later(10)).Value;

        next.Complete(Certificate(), Later(11)).IsSuccess.ShouldBeTrue();

        next.Status.ShouldBe(AssignmentStatus.Completed);
    }

    /// <summary>
    /// The interval counts from the completion's day in Germany: a day the shorter month lacks becomes its last day, and
    /// a completion late in the evening (UTC) that is already the next day in Germany counts from that next day.
    /// </summary>
    [Theory]
    [InlineData("2027-03-31T10:00:00+02:00", 1, "2027-04-30")]
    [InlineData("2027-05-30T22:30:00Z", 2, "2027-07-31")]
    public void A_monthly_follow_up_from_the_31st_is_due_on_the_last_day_of_the_shorter_month(string completedAt, int months, string due)
    {
        var assignment = Assign(DueInDays(30), RecurrenceInterval.From(months)).Value;
        var at = Timestamp.From(DateTimeOffset.Parse(completedAt, CultureInfo.InvariantCulture));
        assignment.Complete(Certificate(), at);

        var next = assignment.FollowUp(AssignmentId.New(), at).Value;

        next.DueDate.ShouldBe(DueDate.From(DateOnly.Parse(due, CultureInfo.InvariantCulture)));
    }

    private static Result<Assignment, AssignmentError> Assign(DueDate dueDate, RecurrenceInterval? recurrence = null) =>
        Assignment.Assign(AssignmentId.New(), Anna, Pharmacy, Hygiene, dueDate, recurrence, Manager, Monday);

    private static DueDate DueInDays(int days) => DueDate.On(Day(days));

    private static CalendarDate Day(int daysFromMonday) => CalendarDate.From(new DateOnly(2027, 3, 1).AddDays(daysFromMonday));

    private static Timestamp Later(int days) => Timestamp.From(Monday.Value.AddDays(days));

    private static CertificateId Certificate() => CertificateId.From(Guid.CreateVersion7());
}
