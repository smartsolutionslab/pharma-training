using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.DTOs;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Assignments;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Courses;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Members;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Tests.Assignments;

/// <summary>
/// An assignment becomes its DTO in one place, <c>AssignmentMappings</c>: its stored status and each change's kind under
/// the endpoints' own names, and its history entry by entry.
/// </summary>
public class AssignmentMappingsTests
{
    private static readonly DateTimeOffset Monday = new(2027, 3, 1, 9, 0, 0, TimeSpan.FromHours(1));
    private static readonly PersonId Manager = PersonId.From(Guid.CreateVersion7());
    private static readonly PersonId Anna = PersonId.From(Guid.CreateVersion7());
    private static readonly DueDate EndOfMarch = DueDate.From(new DateOnly(2027, 3, 31));
    private static readonly DueDate EndOfApril = DueDate.From(new DateOnly(2027, 4, 30));

    [Theory]
    [InlineData(AssignmentStatus.Open)]
    [InlineData(AssignmentStatus.InProgress)]
    [InlineData(AssignmentStatus.Completed)]
    [InlineData(AssignmentStatus.Cancelled)]
    public void Every_status_maps_to_the_status_of_the_same_name(AssignmentStatus status)
    {
        status.ToDto().ToString().ShouldBe(status.ToString());
    }

    [Fact]
    public void The_endpoints_know_exactly_the_statuses_the_domain_stores()
    {
        Enum.GetNames<ComplianceAssignmentStatus>().ShouldBe(Enum.GetNames<AssignmentStatus>());
    }

    [Theory]
    [InlineData(AssignmentChangeKind.DueDateChanged)]
    [InlineData(AssignmentChangeKind.Cancelled)]
    public void Every_change_kind_maps_to_the_kind_of_the_same_name(AssignmentChangeKind kind)
    {
        kind.ToDto().ToString().ShouldBe(kind.ToString());
    }

    [Fact]
    public void The_endpoints_know_exactly_the_change_kinds_the_domain_records()
    {
        Enum.GetNames<ComplianceAssignmentChangeKind>().ShouldBe(Enum.GetNames<AssignmentChangeKind>());
    }

    [Fact]
    public void An_assignment_without_a_recurrence_maps_every_field_and_no_recurrence()
    {
        var course = CourseId.From(Guid.CreateVersion7());
        var pharmacy = PharmacyId.From(Guid.CreateVersion7());
        var assignment = Assignment.Assign(AssignmentId.New(), Anna, pharmacy, course, EndOfMarch, null, Manager, Timestamp.From(Monday)).Value;

        var dto = assignment.ToDto();

        dto.Id.ShouldBe(assignment.Id.Value);
        dto.PharmacyId.ShouldBe(pharmacy.Value);
        dto.LearnerId.ShouldBe(Anna.Value);
        dto.CourseId.ShouldBe(course.Value);
        dto.DueDate.ShouldBe(EndOfMarch.Value);
        dto.RecurrenceMonths.ShouldBeNull();
        dto.AssignedBy.ShouldBe(Manager.Value);
        dto.AssignedAt.ShouldBe(Timestamp.From(Monday).Value);
        dto.Status.ShouldBe(ComplianceAssignmentStatus.Open);
        dto.StartedAt.ShouldBeNull();
        dto.CompletedAt.ShouldBeNull();
        dto.History.ShouldBeEmpty();
    }

    [Fact]
    public void A_completed_yearly_assignment_maps_its_recurrence_and_its_instants()
    {
        var assignment = Assignment.Assign(AssignmentId.New(), Anna, PharmacyId.From(Guid.CreateVersion7()), CourseId.From(Guid.CreateVersion7()), EndOfMarch, RecurrenceInterval.Yearly, Manager, Timestamp.From(Monday)).Value;
        var started = Monday.AddHours(1);
        var completed = Monday.AddHours(2);
        assignment.MarkStarted(Timestamp.From(started));
        assignment.Complete(CertificateId.From(Guid.CreateVersion7()), Timestamp.From(completed)).IsSuccess.ShouldBeTrue();

        var dto = assignment.ToDto();

        dto.RecurrenceMonths.ShouldBe(12);
        dto.Status.ShouldBe(ComplianceAssignmentStatus.Completed);
        dto.StartedAt.ShouldBe(Timestamp.From(started).Value);
        dto.CompletedAt.ShouldBe(Timestamp.From(completed).Value);
    }

    [Fact]
    public void A_due_date_change_maps_field_by_field()
    {
        var at = Timestamp.From(Monday.AddDays(1));
        var change = new AssignmentChange(AssignmentChangeKind.DueDateChanged, Manager, at, EndOfMarch, EndOfApril, ChangeReason.From("Urlaub"));

        var dto = change.ToDto();

        dto.ShouldBe(new ComplianceAssignmentChangeDto(ComplianceAssignmentChangeKind.DueDateChanged, Manager.Value, at.Value, EndOfMarch.Value, EndOfApril.Value, "Urlaub"));
    }

    [Fact]
    public void A_change_without_dates_or_reason_maps_them_to_nothing()
    {
        var at = Timestamp.From(Monday.AddDays(1));
        var change = new AssignmentChange(AssignmentChangeKind.Cancelled, Anna, at, null, null, null);

        var dto = change.ToDto();

        dto.ShouldBe(new ComplianceAssignmentChangeDto(ComplianceAssignmentChangeKind.Cancelled, Anna.Value, at.Value, null, null, null));
    }

    [Fact]
    public void An_assignments_history_maps_entry_by_entry_in_the_order_it_was_kept()
    {
        var assignment = Assignment.Assign(AssignmentId.New(), Anna, PharmacyId.From(Guid.CreateVersion7()), CourseId.From(Guid.CreateVersion7()), EndOfMarch, null, Manager, Timestamp.From(Monday)).Value;
        assignment.ChangeDueDate(EndOfApril, Manager, null, Timestamp.From(Monday.AddDays(1))).IsSuccess.ShouldBeTrue();
        assignment.Cancel(Anna, ChangeReason.MembershipEnded, Timestamp.From(Monday.AddDays(2))).IsSuccess.ShouldBeTrue();

        var dto = assignment.ToDto();

        dto.History.ShouldBe([.. assignment.History.Select(change => change.ToDto())]);
        dto.History.Select(change => change.Kind).ShouldBe([ComplianceAssignmentChangeKind.DueDateChanged, ComplianceAssignmentChangeKind.Cancelled]);
        dto.Status.ShouldBe(ComplianceAssignmentStatus.Cancelled);
    }
}
