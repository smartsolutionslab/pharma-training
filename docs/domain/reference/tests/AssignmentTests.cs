// Compliance — behaviour of the Assignment aggregate (xUnit + Shouldly, the Fabric defaults).

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Tests.Assignments;

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
        Assign(DueInDays(-1)).Error.ShouldBeOfType<DueDateInThePast>();
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

        assignment.Complete(Certificate(), Later(-1)).Error.ShouldBeOfType<CertificatePredatesAssignment>();
    }

    [Fact]
    public void A_completed_assignment_cannot_be_cancelled()
    {
        var assignment = Assign(DueInDays(30)).Value;
        assignment.Complete(Certificate(), Later(1));

        assignment.Cancel(Manager, ChangeReason.From("Versehen"), Later(2)).Error.ShouldBeOfType<AssignmentNotActive>();
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
    public void Three_failed_attempts_show_as_locked_until_the_learner_starts_again()
    {
        var assignment = Assign(DueInDays(30)).Value;
        assignment.MarkStarted(Monday);

        assignment.MarkLocked();
        assignment.StateOn(Day(1)).ShouldBe(AssignmentState.Locked);

        assignment.MarkStarted(Later(2));
        assignment.StateOn(Day(2)).ShouldBe(AssignmentState.InProgress);
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

    private static Result<Assignment, DomainError> Assign(DueDate dueDate, RecurrenceInterval? recurrence = null) =>
        Assignment.Assign(AssignmentId.New(), Anna, Pharmacy, Hygiene, dueDate, recurrence, Manager, Monday);

    private static DueDate DueInDays(int days) => DueDate.On(Day(days));

    private static CalendarDate Day(int daysFromMonday) => CalendarDate.From(new DateOnly(2027, 3, 1).AddDays(daysFromMonday));

    private static Timestamp Later(int days) => Timestamp.From(Monday.Value.AddDays(days));

    private static CertificateId Certificate() => CertificateId.From(Guid.CreateVersion7());
}
