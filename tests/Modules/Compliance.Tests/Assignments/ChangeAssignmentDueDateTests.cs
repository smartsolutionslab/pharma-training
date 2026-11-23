using System.Net;
using Microsoft.Extensions.Time.Testing;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.Commands.ChangeAssignmentDueDate;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Assignments;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Courses;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Members;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Tests.Members;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Tests.Assignments;

/// <summary>
/// A manager changes an assignment's due date (#30): who may is decided like assigning (docs/adr/0003), the new day obeys
/// the same rules, the change is kept in the assignment's history with who, when, from, to and why, and a save that lost
/// a race is told so (docs/adr/0002). Members are seeded through their store; the assignment store is the unit of work
/// the handler commits.
/// </summary>
public class ChangeAssignmentDueDateTests
{
    private static readonly DateTimeOffset Monday = new(2027, 3, 1, 9, 0, 0, TimeSpan.FromHours(1));
    private static readonly DateTimeOffset Tuesday = Monday.AddDays(1);
    private static readonly PharmacyId Pharmacy = PharmacyId.From(Guid.CreateVersion7());
    private static readonly CourseId Hygiene = CourseId.From(Guid.CreateVersion7());
    private static readonly PersonId Manager = PersonId.From(Guid.CreateVersion7());
    private static readonly PersonId Anna = PersonId.From(Guid.CreateVersion7());
    private static readonly DueDate EndOfMarch = DueDate.From(new DateOnly(2027, 3, 31));
    private static readonly DueDate EndOfApril = DueDate.From(new DateOnly(2027, 4, 30));

    private readonly FakeTimeProvider time = new(Monday);
    private readonly InMemoryMemberStore members = new();
    private readonly InMemoryAssignmentStore assignments = new();

    [Fact]
    public async Task A_manager_changes_the_due_date_and_the_history_says_who_when_from_what_to_what_and_why()
    {
        var assignment = await SeedAsync();
        time.SetUtcNow(Tuesday);

        var result = await ChangeAsync(assignment.Id, EndOfApril, ChangeReason.From("Urlaub"));

        result.IsSuccess.ShouldBeTrue();
        assignment.DueDate.ShouldBe(EndOfApril);
        var change = assignment.History.ShouldHaveSingleItem();
        change.Kind.ShouldBe(AssignmentChangeKind.DueDateChanged);
        change.ChangedBy.ShouldBe(Manager);
        change.ChangedAt.ShouldBe(Timestamp.From(Tuesday));
        change.PreviousDueDate.ShouldBe(EndOfMarch);
        change.NewDueDate.ShouldBe(EndOfApril);
        change.Reason.ShouldBe(ChangeReason.From("Urlaub"));
        assignments.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task A_change_without_a_reason_is_kept_without_one()
    {
        var assignment = await SeedAsync();

        var result = await ChangeAsync(assignment.Id, EndOfApril, reason: null);

        result.IsSuccess.ShouldBeTrue();
        assignment.History.ShouldHaveSingleItem().Reason.ShouldBeNull();
    }

    [Fact]
    public async Task The_same_due_date_again_succeeds_and_leaves_no_history()
    {
        var assignment = await SeedAsync();

        var result = await ChangeAsync(assignment.Id, EndOfMarch, ChangeReason.From("Urlaub"));

        result.IsSuccess.ShouldBeTrue();
        assignment.DueDate.ShouldBe(EndOfMarch);
        assignment.History.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_due_date_of_an_assignment_in_progress_can_be_changed()
    {
        var assignment = await SeedAsync();
        assignment.MarkStarted(Timestamp.From(Monday.AddHours(1)));

        var result = await ChangeAsync(assignment.Id, EndOfApril, reason: null);

        result.IsSuccess.ShouldBeTrue();
        assignment.DueDate.ShouldBe(EndOfApril);
        assignment.Status.ShouldBe(AssignmentStatus.InProgress);
    }

    [Fact]
    public async Task An_employee_may_not_change_a_due_date()
    {
        var assignment = await SeedAsync();

        var result = await ChangeAsync(assignment.Id, EndOfApril, reason: null, by: Anna);

        result.Error.Code.ShouldBe("compliance.capability-missing");
        result.Error.Status.ShouldBe(HttpStatusCode.Forbidden);
        assignment.DueDate.ShouldBe(EndOfMarch);
        assignment.History.ShouldBeEmpty();
        assignments.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task A_stranger_is_told_the_pharmacy_does_not_exist()
    {
        var assignment = await SeedAsync();

        var result = await ChangeAsync(assignment.Id, EndOfApril, reason: null, by: PersonId.From(Guid.CreateVersion7()));

        result.Error.Code.ShouldBe("compliance.pharmacy-not-found");
        result.Error.Status.ShouldBe(HttpStatusCode.NotFound);
        assignment.DueDate.ShouldBe(EndOfMarch);
        assignments.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task An_unknown_assignment_is_not_found()
    {
        await SeedAsync();

        var result = await ChangeAsync(AssignmentId.New(), EndOfApril, reason: null);

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

        var result = await ChangeAsync(elsewhere.Id, EndOfApril, reason: null);

        result.Error.Code.ShouldBe("compliance.assignment-not-found");
        result.Error.Status.ShouldBe(HttpStatusCode.NotFound);
        elsewhere.DueDate.ShouldBe(EndOfMarch);
        elsewhere.History.ShouldBeEmpty();
        assignments.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task A_due_date_in_the_past_cannot_be_processed_and_nothing_changes()
    {
        var assignment = await SeedAsync();

        var result = await ChangeAsync(assignment.Id, DueDate.From(new DateOnly(2027, 2, 28)), reason: null);

        result.Error.Code.ShouldBe("compliance.due-date-in-the-past");
        result.Error.Status.ShouldBe(HttpStatusCode.UnprocessableEntity);
        assignment.DueDate.ShouldBe(EndOfMarch);
        assignment.History.ShouldBeEmpty();
        assignments.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task A_due_date_more_than_ten_years_ahead_is_refused_as_when_assigning()
    {
        var assignment = await SeedAsync();

        var result = await ChangeAsync(assignment.Id, DueDate.From(new DateOnly(2037, 3, 2)), reason: null);

        result.Error.Code.ShouldBe("compliance.due-date-too-far");
        result.Error.Status.ShouldBe(HttpStatusCode.BadRequest);
        assignment.DueDate.ShouldBe(EndOfMarch);
        assignments.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task A_due_date_exactly_ten_years_ahead_is_accepted()
    {
        var assignment = await SeedAsync();

        var result = await ChangeAsync(assignment.Id, DueDate.From(new DateOnly(2037, 3, 1)), reason: null);

        result.IsSuccess.ShouldBeTrue();
        assignment.DueDate.ShouldBe(DueDate.From(new DateOnly(2037, 3, 1)));
    }

    [Theory]
    [InlineData(AssignmentStatus.Completed)]
    [InlineData(AssignmentStatus.Cancelled)]
    public async Task The_due_date_of_an_assignment_no_longer_active_cannot_be_changed(AssignmentStatus status)
    {
        var assignment = await SeedAsync();
        End(assignment, status);

        var result = await ChangeAsync(assignment.Id, EndOfApril, reason: null);

        result.Error.Code.ShouldBe("compliance.assignment-not-active");
        result.Error.Status.ShouldBe(HttpStatusCode.Conflict);
        assignment.DueDate.ShouldBe(EndOfMarch);
        assignment.History.ShouldNotContain(change => change.Kind == AssignmentChangeKind.DueDateChanged);
        assignments.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task A_save_that_lost_a_race_is_told_the_assignments_changed()
    {
        var assignment = await SeedAsync();
        assignments.LoseNextCommit();

        var result = await ChangeAsync(assignment.Id, EndOfApril, reason: null);

        result.Error.Code.ShouldBe("compliance.assignments-changed");
        result.Error.Status.ShouldBe(HttpStatusCode.Conflict);
        assignments.Commits.ShouldBe(0);
    }

    private static Assignment Assigned(PharmacyId pharmacy) =>
        Assignment.Assign(AssignmentId.New(), Anna, pharmacy, Hygiene, EndOfMarch, null, Manager, Timestamp.From(Monday)).Value;

    private static void End(Assignment assignment, AssignmentStatus status)
    {
        if (status == AssignmentStatus.Completed)
        {
            assignment.Complete(CertificateId.From(Guid.CreateVersion7()), Timestamp.From(Monday.AddHours(2))).IsSuccess.ShouldBeTrue();
        }
        else
        {
            assignment.Cancel(Manager, ChangeReason.From("Versehen"), Timestamp.From(Monday.AddHours(2))).IsSuccess.ShouldBeTrue();
        }
    }

    /// <summary>The manager and Anna as members of the pharmacy, and an open assignment of Anna's there, due at the end of March.</summary>
    private async Task<Assignment> SeedAsync()
    {
        await ComplianceMembers.StartAsync(members, Manager, Pharmacy, MembershipRole.Manager);
        await ComplianceMembers.StartAsync(members, Anna, Pharmacy, MembershipRole.Employee);
        var assignment = Assigned(Pharmacy);
        assignments.Stored.Add(assignment);
        return assignment;
    }

    private Task<Result<Unit, ApiError>> ChangeAsync(AssignmentId assignment, DueDate dueDate, ChangeReason? reason, PersonId? by = null) =>
        new ChangeAssignmentDueDateHandler(members, assignments, assignments, time)
            .HandleAsync(new ChangeAssignmentDueDate(Pharmacy, assignment, dueDate, reason, by ?? Manager), TestContext.Current.CancellationToken);
}
