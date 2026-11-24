using System.Net;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.DTOs;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.Queries.GetAssignment;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Assignments;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Courses;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Members;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Tests.Assignments;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Tests.Members;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Tests.Queries;

/// <summary>
/// A manager reads one assignment of their pharmacy with its history (#30: who changed what when), whatever its status;
/// who may is decided like assigning (docs/adr/0003), and an assignment of another pharmacy is not found through this one.
/// </summary>
public class GetAssignmentTests
{
    private static readonly DateTimeOffset Monday = new(2027, 3, 1, 9, 0, 0, TimeSpan.FromHours(1));
    private static readonly PharmacyId Pharmacy = PharmacyId.From(Guid.CreateVersion7());
    private static readonly CourseId Hygiene = CourseId.From(Guid.CreateVersion7());
    private static readonly PersonId Manager = PersonId.From(Guid.CreateVersion7());
    private static readonly PersonId Anna = PersonId.From(Guid.CreateVersion7());
    private static readonly DueDate EndOfMarch = DueDate.From(new DateOnly(2027, 3, 31));
    private static readonly DueDate EndOfApril = DueDate.From(new DateOnly(2027, 4, 30));

    private readonly InMemoryMemberStore members = new();
    private readonly InMemoryAssignmentStore assignments = new();

    [Fact]
    public async Task A_manager_reads_an_assignment_with_its_history_oldest_first()
    {
        var assignment = await SeedAsync();
        var started = Monday.AddHours(1);
        var changed = Monday.AddDays(1);
        var cancelled = Monday.AddDays(2);
        assignment.MarkStarted(Timestamp.From(started));
        assignment.ChangeDueDate(EndOfApril, Manager, ChangeReason.From("Urlaub"), Timestamp.From(changed)).IsSuccess.ShouldBeTrue();
        assignment.Cancel(Manager, ChangeReason.Withdrawn, Timestamp.From(cancelled)).IsSuccess.ShouldBeTrue();

        var result = await GetAsync(assignment.Id);

        var dto = result.Value;
        dto.Id.ShouldBe(assignment.Id.Value);
        dto.PharmacyId.ShouldBe(Pharmacy.Value);
        dto.LearnerId.ShouldBe(Anna.Value);
        dto.CourseId.ShouldBe(Hygiene.Value);
        dto.DueDate.ShouldBe(EndOfApril.Value);
        dto.RecurrenceMonths.ShouldBe(12);
        dto.AssignedBy.ShouldBe(Manager.Value);
        dto.AssignedAt.ShouldBe(Timestamp.From(Monday).Value);
        dto.Status.ShouldBe(ComplianceAssignmentStatus.Cancelled);
        dto.StartedAt.ShouldBe(Timestamp.From(started).Value);
        dto.CompletedAt.ShouldBeNull();
        dto.History.ShouldBe(
        [
            new ComplianceAssignmentChangeDto(ComplianceAssignmentChangeKind.DueDateChanged, Manager.Value, Timestamp.From(changed).Value, EndOfMarch.Value, EndOfApril.Value, "Urlaub"),
            new ComplianceAssignmentChangeDto(ComplianceAssignmentChangeKind.Cancelled, Manager.Value, Timestamp.From(cancelled).Value, null, null, ChangeReason.Withdrawn.Value),
        ]);
    }

    [Fact]
    public async Task An_assignment_never_changed_has_an_empty_history()
    {
        var assignment = await SeedAsync();

        var result = await GetAsync(assignment.Id);

        result.Value.Status.ShouldBe(ComplianceAssignmentStatus.Open);
        result.Value.History.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_employee_may_not_read_an_assignment()
    {
        var assignment = await SeedAsync();

        var result = await GetAsync(assignment.Id, by: Anna);

        result.Error.Code.ShouldBe("compliance.capability-missing");
        result.Error.Status.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_stranger_is_told_the_pharmacy_does_not_exist()
    {
        var assignment = await SeedAsync();

        var result = await GetAsync(assignment.Id, by: PersonId.From(Guid.CreateVersion7()));

        result.Error.Code.ShouldBe("compliance.pharmacy-not-found");
        result.Error.Status.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task An_unknown_assignment_is_not_found()
    {
        await SeedAsync();

        var result = await GetAsync(AssignmentId.New());

        result.Error.Code.ShouldBe("compliance.assignment-not-found");
        result.Error.Status.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task An_assignment_of_another_pharmacy_is_not_found_through_this_one()
    {
        await SeedAsync();
        var otherPharmacy = PharmacyId.From(Guid.CreateVersion7());
        await ComplianceMembers.StartAsync(members, Manager, otherPharmacy, MembershipRole.Manager);
        var elsewhere = Assigned(otherPharmacy);
        assignments.Stored.Add(elsewhere);

        var result = await GetAsync(elsewhere.Id);

        result.Error.Code.ShouldBe("compliance.assignment-not-found");
        result.Error.Status.ShouldBe(HttpStatusCode.NotFound);
    }

    private static Assignment Assigned(PharmacyId pharmacy) =>
        Assignment.Assign(AssignmentId.New(), Anna, pharmacy, Hygiene, EndOfMarch, RecurrenceInterval.Yearly, Manager, Timestamp.From(Monday)).Value;

    /// <summary>The manager and Anna as members of the pharmacy, and a yearly assignment of Anna's there, due at the end of March.</summary>
    private async Task<Assignment> SeedAsync()
    {
        await ComplianceMembers.StartAsync(members, Manager, Pharmacy, MembershipRole.Manager);
        await ComplianceMembers.StartAsync(members, Anna, Pharmacy, MembershipRole.Employee);
        var assignment = Assigned(Pharmacy);
        assignments.Stored.Add(assignment);
        return assignment;
    }

    private Task<Result<ComplianceAssignmentDto, ApiError>> GetAsync(AssignmentId assignment, PersonId? by = null) =>
        new GetAssignmentHandler(members, assignments)
            .HandleAsync(new GetAssignment(Pharmacy, assignment, by ?? Manager), TestContext.Current.CancellationToken);
}
