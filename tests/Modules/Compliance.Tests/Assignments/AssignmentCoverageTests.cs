using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Assignments;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Assignments.Events;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Courses;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Members;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Tests.Assignments;

/// <summary>What the reference tests leave out of the Assignment aggregate: each refusal of each step, and each error's code.</summary>
public class AssignmentCoverageTests
{
    private static readonly Timestamp Monday = Timestamp.From(new DateTimeOffset(2027, 3, 1, 9, 0, 0, TimeSpan.FromHours(1)));
    private static readonly PersonId Anna = PersonId.From(Guid.CreateVersion7());
    private static readonly PersonId Manager = PersonId.From(Guid.CreateVersion7());
    private static readonly PharmacyId Pharmacy = PharmacyId.From(Guid.CreateVersion7());
    private static readonly CourseId Hygiene = CourseId.From(Guid.CreateVersion7());

    [Fact]
    public void Assigning_keeps_who_assigned_when_and_what()
    {
        var id = AssignmentId.New();

        var assignment = Assignment.Assign(id, Anna, Pharmacy, Hygiene, DueInDays(30), RecurrenceInterval.Yearly, Manager, Monday).Value;

        assignment.Id.ShouldBe(id);
        assignment.Learner.ShouldBe(Anna);
        assignment.Pharmacy.ShouldBe(Pharmacy);
        assignment.Course.ShouldBe(Hygiene);
        assignment.DueDate.ShouldBe(DueInDays(30));
        assignment.Recurrence.ShouldBe(RecurrenceInterval.Yearly);
        assignment.AssignedBy.ShouldBe(Manager);
        assignment.AssignedAt.ShouldBe(Monday);
        assignment.TestAccess.ShouldBe(TestAccess.Available);
        assignment.StartedAt.ShouldBeNull();
        assignment.CompletedAt.ShouldBeNull();
        assignment.Certificate.ShouldBeNull();
        assignment.History.ShouldBeEmpty();
        assignment.DomainEvents.ShouldHaveSingleItem().ShouldBe(new CourseAssigned(id, Anna, Pharmacy, Hygiene, DueInDays(30)));
    }

    [Fact]
    public void Changing_the_due_date_of_a_cancelled_assignment_is_refused()
    {
        var assignment = Assign(DueInDays(30));
        assignment.Cancel(Manager, ChangeReason.From("Versehen"), Later(1));

        var refusal = assignment.ChangeDueDate(DueInDays(60), Manager, null, Later(2)).Error.Value.ShouldBeOfType<AssignmentNotActive>();

        refusal.Assignment.ShouldBe(assignment.Id);
        refusal.Status.ShouldBe(AssignmentStatus.Cancelled);
        assignment.DueDate.ShouldBe(DueInDays(30));
    }

    [Fact]
    public void Changing_the_due_date_to_the_past_is_refused()
    {
        var assignment = Assign(DueInDays(30));

        var refusal = assignment.ChangeDueDate(DueInDays(1), Manager, null, Later(2)).Error.Value.ShouldBeOfType<DueDateInThePast>();

        refusal.DueDate.ShouldBe(DueInDays(1));
        assignment.DueDate.ShouldBe(DueInDays(30));
        assignment.History.ShouldBeEmpty();
    }

    [Fact]
    public void Changing_the_due_date_to_the_same_day_changes_nothing()
    {
        var assignment = Assign(DueInDays(30));
        assignment.ClearDomainEvents();

        assignment.ChangeDueDate(DueInDays(30), Manager, ChangeReason.From("Urlaub"), Later(1)).IsSuccess.ShouldBeTrue();

        assignment.History.ShouldBeEmpty();
        assignment.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Moving_the_due_date_announces_the_new_one()
    {
        var assignment = Assign(DueInDays(30));
        assignment.ClearDomainEvents();

        assignment.ChangeDueDate(DueInDays(60), Manager, null, Later(1)).IsSuccess.ShouldBeTrue();

        assignment.DueDate.ShouldBe(DueInDays(60));
        assignment.DomainEvents.ShouldHaveSingleItem().ShouldBe(new AssignmentDueDateChanged(assignment.Id, DueInDays(60)));
        var change = assignment.History.ShouldHaveSingleItem();
        change.ChangedAt.ShouldBe(Later(1));
        change.Reason.ShouldBeNull();
    }

    [Fact]
    public void Cancelling_leaves_a_trace_and_announces_it()
    {
        var assignment = Assign(DueInDays(30));
        assignment.ClearDomainEvents();

        assignment.Cancel(Manager, ChangeReason.From("Versehen"), Later(1)).IsSuccess.ShouldBeTrue();

        assignment.Status.ShouldBe(AssignmentStatus.Cancelled);
        var change = assignment.History.ShouldHaveSingleItem();
        change.Kind.ShouldBe(AssignmentChangeKind.Cancelled);
        change.ChangedBy.ShouldBe(Manager);
        change.ChangedAt.ShouldBe(Later(1));
        change.Reason.ShouldBe(ChangeReason.From("Versehen"));
        change.PreviousDueDate.ShouldBeNull();
        change.NewDueDate.ShouldBeNull();
        assignment.DomainEvents.ShouldHaveSingleItem().ShouldBe(new AssignmentCancelled(assignment.Id, Anna, Pharmacy));
    }

    [Fact]
    public void Cancelling_twice_leaves_one_trace_and_one_announcement()
    {
        var assignment = Assign(DueInDays(30));
        assignment.ClearDomainEvents();
        assignment.Cancel(Manager, ChangeReason.MembershipEnded, Later(1));

        assignment.Cancel(Manager, ChangeReason.MembershipEnded, Later(2)).IsSuccess.ShouldBeTrue();

        assignment.History.ShouldHaveSingleItem().Reason.ShouldBe(ChangeReason.MembershipEnded);
        assignment.DomainEvents.ShouldHaveSingleItem();
    }

    [Fact]
    public void A_lock_before_the_start_changes_nothing()
    {
        var assignment = Assign(DueInDays(30));

        assignment.MarkLocked();

        assignment.TestAccess.ShouldBe(TestAccess.Available);
        assignment.StateOn(Day(1)).ShouldBe(AssignmentState.Open);
    }

    [Fact]
    public void Completing_keeps_the_certificate_and_announces_it()
    {
        var assignment = Assign(DueInDays(30));
        assignment.MarkStarted(Later(1));
        assignment.MarkLocked();
        assignment.ClearDomainEvents();
        var certificate = Certificate();

        assignment.Complete(certificate, Later(5)).IsSuccess.ShouldBeTrue();

        assignment.Status.ShouldBe(AssignmentStatus.Completed);
        assignment.TestAccess.ShouldBe(TestAccess.Available);
        assignment.CompletedAt.ShouldBe(Later(5));
        assignment.Certificate.ShouldBe(certificate);
        assignment.DomainEvents.ShouldHaveSingleItem()
            .ShouldBe(new AssignmentCompleted(assignment.Id, Anna, Pharmacy, Hygiene, certificate, Later(5), DueInDays(30)));
    }

    [Fact]
    public void A_second_certificate_does_not_complete_a_completed_assignment()
    {
        var assignment = Assign(DueInDays(30));
        var first = Certificate();
        assignment.Complete(first, Later(1));

        var refusal = assignment.Complete(Certificate(), Later(2)).Error.Value.ShouldBeOfType<AssignmentNotActive>();

        refusal.Status.ShouldBe(AssignmentStatus.Completed);
        assignment.Certificate.ShouldBe(first);
        assignment.CompletedAt.ShouldBe(Later(1));
    }

    [Fact]
    public void A_certificate_does_not_complete_a_cancelled_assignment()
    {
        var assignment = Assign(DueInDays(30));
        assignment.Cancel(Manager, ChangeReason.From("Versehen"), Later(1));

        assignment.Complete(Certificate(), Later(2)).Error.Value.ShouldBeOfType<AssignmentNotActive>().Status.ShouldBe(AssignmentStatus.Cancelled);
    }

    [Fact]
    public void A_follow_up_of_an_open_assignment_is_none()
    {
        var assignment = Assign(DueInDays(30), RecurrenceInterval.Yearly);

        assignment.FollowUp(AssignmentId.New(), Later(10)).HasValue.ShouldBeFalse();
    }

    [Fact]
    public void A_follow_up_is_for_the_same_learner_pharmacy_and_course_and_announces_itself()
    {
        var assignment = Assign(DueInDays(30), RecurrenceInterval.Yearly);
        assignment.Complete(Certificate(), Later(10));
        var id = AssignmentId.New();

        var next = assignment.FollowUp(id, Later(10)).Value;

        next.Id.ShouldBe(id);
        next.Learner.ShouldBe(Anna);
        next.Pharmacy.ShouldBe(Pharmacy);
        next.Course.ShouldBe(Hygiene);
        next.Recurrence.ShouldBe(RecurrenceInterval.Yearly);
        next.AssignedBy.ShouldBe(Manager);
        next.AssignedAt.ShouldBe(Later(10));
        next.DomainEvents.ShouldHaveSingleItem().ShouldBe(new CourseAssigned(id, Anna, Pharmacy, Hygiene, next.DueDate));
    }

    [Theory]
    [InlineData("Cancelled", 40, "Cancelled")]
    [InlineData("Completed", 40, "Completed")]
    [InlineData("InProgress", 10, "InProgress")]
    [InlineData("InProgress", 31, "Overdue")]
    public void The_state_on_a_day_follows_status_and_due_date(string status, int daysFromMonday, string expected)
    {
        var assignment = Assign(DueInDays(30));
        switch (status)
        {
            case "Cancelled":
                assignment.Cancel(Manager, ChangeReason.From("Versehen"), Later(1));
                break;
            case "Completed":
                assignment.Complete(Certificate(), Later(20));
                break;
            default:
                assignment.MarkStarted(Later(1));
                break;
        }

        assignment.Status.ShouldBe(Enum.Parse<AssignmentStatus>(status));
        assignment.StateOn(Day(daysFromMonday)).ShouldBe(Enum.Parse<AssignmentState>(expected));
    }

    [Theory]
    [InlineData("DueDateInThePast", "compliance.due-date-in-the-past")]
    [InlineData("AssignmentNotActive", "compliance.assignment-not-active")]
    [InlineData("CertificatePredatesAssignment", "compliance.certificate-predates-assignment")]
    [InlineData("AssignmentAlreadyActive", "compliance.assignment-already-active")]
    public void Each_error_carries_its_code(string name, string code)
    {
        DomainError error = name switch
        {
            "DueDateInThePast" => AssignmentErrors.DueDateInThePast(DueInDays(-1)).Value.ShouldBeAssignableTo<DomainError>().ShouldNotBeNull(),
            "AssignmentNotActive" => AssignmentErrors.NotActive(AssignmentId.New(), AssignmentStatus.Completed).Value.ShouldBeAssignableTo<DomainError>().ShouldNotBeNull(),
            "CertificatePredatesAssignment" => AssignmentErrors.CertificatePredatesAssignment(AssignmentId.New(), Certificate()).Value.ShouldBeAssignableTo<DomainError>().ShouldNotBeNull(),
            "AssignmentAlreadyActive" => AssignmentErrors.AlreadyActive(Anna, Hygiene),
            _ => throw new InvalidOperationException($"No assignment error is named {name}."),
        };

        error.GetType().Name.ShouldBe(name);
        error.Code.ShouldBe(code);
        error.Message.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void A_due_date_in_the_past_names_the_date_in_german_form()
    {
        var error = AssignmentErrors.DueDateInThePast(DueDate.From(new DateOnly(2027, 2, 28))).Value.ShouldBeOfType<DueDateInThePast>();

        error.Message.ShouldContain("28.02.2027");
    }

    private static Assignment Assign(DueDate dueDate, RecurrenceInterval? recurrence = null) =>
        Assignment.Assign(AssignmentId.New(), Anna, Pharmacy, Hygiene, dueDate, recurrence, Manager, Monday).Value;

    private static DueDate DueInDays(int days) => DueDate.On(Day(days));

    private static CalendarDate Day(int daysFromMonday) => CalendarDate.From(new DateOnly(2027, 3, 1).AddDays(daysFromMonday));

    private static Timestamp Later(int days) => Timestamp.From(Monday.Value.AddDays(days));

    private static CertificateId Certificate() => CertificateId.From(Guid.CreateVersion7());
}
