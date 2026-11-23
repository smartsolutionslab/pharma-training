using System.Net;
using Microsoft.Extensions.Time.Testing;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.Commands.CancelAssignment;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Assignments;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Courses;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Members;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Tests.Members;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Tests.Assignments;

/// <summary>
/// A manager withdraws an assignment (#30): an open or in-progress one is cancelled with a history entry by the manager
/// and the fixed reason <see cref="ChangeReason.Withdrawn"/>, a second withdrawal changes nothing more, a completed one is
/// refused, and a save that lost a race is told so (docs/adr/0002).
/// </summary>
public class CancelAssignmentTests
{
    private static readonly DateTimeOffset Monday = new(2027, 3, 1, 9, 0, 0, TimeSpan.FromHours(1));
    private static readonly DateTimeOffset Tuesday = Monday.AddDays(1);
    private static readonly PharmacyId Pharmacy = PharmacyId.From(Guid.CreateVersion7());
    private static readonly CourseId Hygiene = CourseId.From(Guid.CreateVersion7());
    private static readonly PersonId Manager = PersonId.From(Guid.CreateVersion7());
    private static readonly PersonId Anna = PersonId.From(Guid.CreateVersion7());
    private static readonly DueDate EndOfMarch = DueDate.From(new DateOnly(2027, 3, 31));

    private readonly FakeTimeProvider time = new(Monday);
    private readonly InMemoryMemberStore members = new();
    private readonly InMemoryAssignmentStore assignments = new();

    [Theory]
    [InlineData(AssignmentStatus.Open)]
    [InlineData(AssignmentStatus.InProgress)]
    public async Task A_manager_withdraws_an_active_assignment_and_the_history_says_who_when_and_why(AssignmentStatus status)
    {
        var assignment = await SeedAsync();
        if (status == AssignmentStatus.InProgress)
        {
            assignment.MarkStarted(Timestamp.From(Monday.AddHours(1)));
        }

        time.SetUtcNow(Tuesday);

        var result = await CancelAsync(assignment.Id);

        result.IsSuccess.ShouldBeTrue();
        assignment.Status.ShouldBe(AssignmentStatus.Cancelled);
        var change = assignment.History.ShouldHaveSingleItem();
        change.Kind.ShouldBe(AssignmentChangeKind.Cancelled);
        change.ChangedBy.ShouldBe(Manager);
        change.ChangedAt.ShouldBe(Timestamp.From(Tuesday));
        change.PreviousDueDate.ShouldBeNull();
        change.NewDueDate.ShouldBeNull();
        change.Reason.ShouldBe(ChangeReason.Withdrawn);
        assignments.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task Withdrawing_again_succeeds_and_leaves_one_entry()
    {
        var assignment = await SeedAsync();
        (await CancelAsync(assignment.Id)).IsSuccess.ShouldBeTrue();

        var again = await CancelAsync(assignment.Id);

        again.IsSuccess.ShouldBeTrue();
        assignment.Status.ShouldBe(AssignmentStatus.Cancelled);
        assignment.History.ShouldHaveSingleItem().Kind.ShouldBe(AssignmentChangeKind.Cancelled);
    }

    [Fact]
    public async Task A_completed_assignment_cannot_be_withdrawn()
    {
        var assignment = await SeedAsync();
        assignment.Complete(CertificateId.From(Guid.CreateVersion7()), Timestamp.From(Monday.AddHours(2))).IsSuccess.ShouldBeTrue();

        var result = await CancelAsync(assignment.Id);

        result.Error.Code.ShouldBe("compliance.assignment-not-active");
        result.Error.Status.ShouldBe(HttpStatusCode.Conflict);
        assignment.Status.ShouldBe(AssignmentStatus.Completed);
        assignment.History.ShouldBeEmpty();
        assignments.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task An_employee_may_not_withdraw()
    {
        var assignment = await SeedAsync();

        var result = await CancelAsync(assignment.Id, by: Anna);

        result.Error.Code.ShouldBe("compliance.capability-missing");
        result.Error.Status.ShouldBe(HttpStatusCode.Forbidden);
        assignment.Status.ShouldBe(AssignmentStatus.Open);
        assignment.History.ShouldBeEmpty();
        assignments.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task A_stranger_is_told_the_pharmacy_does_not_exist()
    {
        var assignment = await SeedAsync();

        var result = await CancelAsync(assignment.Id, by: PersonId.From(Guid.CreateVersion7()));

        result.Error.Code.ShouldBe("compliance.pharmacy-not-found");
        result.Error.Status.ShouldBe(HttpStatusCode.NotFound);
        assignment.Status.ShouldBe(AssignmentStatus.Open);
        assignments.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task An_unknown_assignment_is_not_found()
    {
        await SeedAsync();

        var result = await CancelAsync(AssignmentId.New());

        result.Error.Code.ShouldBe("compliance.assignment-not-found");
        result.Error.Status.ShouldBe(HttpStatusCode.NotFound);
        assignments.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task An_assignment_of_another_pharmacy_is_not_found_through_this_one()
    {
        await SeedAsync();
        var otherPharmacy = PharmacyId.From(Guid.CreateVersion7());
        await ComplianceMembers.StartAsync(members, Manager, otherPharmacy, MembershipRole.Manager);
        await ComplianceMembers.StartAsync(members, Anna, otherPharmacy, MembershipRole.Employee);
        var elsewhere = Assigned(otherPharmacy);
        assignments.Stored.Add(elsewhere);

        var result = await CancelAsync(elsewhere.Id);

        result.Error.Code.ShouldBe("compliance.assignment-not-found");
        result.Error.Status.ShouldBe(HttpStatusCode.NotFound);
        elsewhere.Status.ShouldBe(AssignmentStatus.Open);
        elsewhere.History.ShouldBeEmpty();
        assignments.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task A_save_that_lost_a_race_is_told_the_assignments_changed()
    {
        var assignment = await SeedAsync();
        assignments.LoseNextCommit();

        var result = await CancelAsync(assignment.Id);

        result.Error.Code.ShouldBe("compliance.assignments-changed");
        result.Error.Status.ShouldBe(HttpStatusCode.Conflict);
        assignments.Commits.ShouldBe(0);
    }

    private static Assignment Assigned(PharmacyId pharmacy) =>
        Assignment.Assign(AssignmentId.New(), Anna, pharmacy, Hygiene, EndOfMarch, null, Manager, Timestamp.From(Monday)).Value;

    /// <summary>The manager and Anna as members of the pharmacy, and an open assignment of Anna's there.</summary>
    private async Task<Assignment> SeedAsync()
    {
        await ComplianceMembers.StartAsync(members, Manager, Pharmacy, MembershipRole.Manager);
        await ComplianceMembers.StartAsync(members, Anna, Pharmacy, MembershipRole.Employee);
        var assignment = Assigned(Pharmacy);
        assignments.Stored.Add(assignment);
        return assignment;
    }

    private Task<Result<Unit, ApiError>> CancelAsync(AssignmentId assignment, PersonId? by = null) =>
        new CancelAssignmentHandler(members, assignments, assignments, time)
            .HandleAsync(new CancelAssignment(Pharmacy, assignment, by ?? Manager), TestContext.Current.CancellationToken);
}
