using System.Net;
using Microsoft.Extensions.Time.Testing;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.DTOs;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.Queries.GetComplianceMatrix;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Assignments;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Courses;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Members;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Progress;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Tests.Assignments;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Tests.Courses;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Tests.Members;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Tests.Progress;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Tests.Queries;

/// <summary>
/// The team matrix (#34): the pharmacy's active members by the courses with a non-cancelled assignment there, each cell
/// the state its latest non-cancelled assignment shows today in German time and, once completed, the day it stays valid
/// until, and the compliance rate, the share of cells that are compliant (open, in progress or locked, or completed and
/// still valid), over every filtered row, in total and per course; only for a caller whose current role there grants
/// viewing the team's compliance (docs/adr/0003, docs/adr/0015).
/// </summary>
public class GetComplianceMatrixTests
{
    // Monday 2026-11-23, 10:00 in Berlin.
    private static readonly DateTimeOffset Now = new(2026, 11, 23, 9, 0, 0, TimeSpan.Zero);
    private static readonly Timestamp Earlier = Timestamp.From(new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero));
    private static readonly DateOnly InThreeWeeks = new(2026, 12, 15);
    private static readonly DateOnly LastFriday = new(2026, 11, 20);
    private static readonly PharmacyId Pharmacy = PharmacyId.From(Guid.CreateVersion7());
    private static readonly PersonId Manager = PersonId.From(Guid.CreateVersion7());
    private static readonly PersonId Anna = PersonId.From(Guid.CreateVersion7());
    private static readonly PersonId Berta = PersonId.From(Guid.CreateVersion7());

    private readonly InMemoryMemberStore members = new();
    private readonly InMemoryAssignmentStore assignments = new();
    private readonly InMemoryCourseVersionStore courses = new();
    private readonly InMemoryLearnerProgressStore progress = new();
    private readonly FakeTimeProvider time = new(Now);

    [Fact]
    public async Task A_signed_in_non_member_is_told_the_pharmacy_does_not_exist()
    {
        await TeamAsync();

        var result = await MatrixAsync(PersonId.From(Guid.CreateVersion7()));

        result.IsSuccess.ShouldBeFalse();
        result.Error.Code.ShouldBe("compliance.pharmacy-not-found");
        result.Error.Status.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task An_employee_is_refused_for_lack_of_the_capability()
    {
        await TeamAsync();

        var result = await MatrixAsync(Anna);

        result.IsSuccess.ShouldBeFalse();
        result.Error.Code.ShouldBe("compliance.capability-missing");
        result.Error.Status.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_pharmacy_without_assignments_has_no_columns_its_members_as_empty_rows_and_no_rate()
    {
        await TeamAsync();
        Course("Hygiene");

        var matrix = (await MatrixAsync(Manager)).Value;

        matrix.Courses.ShouldBeEmpty();
        matrix.Members.Items.Select(row => row.Name).ShouldBe(["Anna Arndt", "Berta Braun", "Mara Manager"]);
        matrix.Members.Items.ShouldAllBe(row => row.Cells.Count == 0);
        matrix.Members.TotalCount.ShouldBe(3);
        matrix.Assignments.ShouldBe(0);
        matrix.CompliantAssignments.ShouldBe(0);
        matrix.OverdueAssignments.ShouldBe(0);
        matrix.ComplianceRate.ShouldBeNull();
    }

    [Fact]
    public async Task Only_courses_assigned_in_this_pharmacy_are_columns_sorted_by_title()
    {
        await TeamAsync();
        var hygiene = Course("Hygiene");
        var firstAid = Course("erste Hilfe");
        var elsewhere = Course("Andernorts");
        Course("Nie zugewiesen");
        Assigned(Anna, hygiene, InThreeWeeks);
        Assigned(Berta, firstAid, InThreeWeeks);
        Assigned(Anna, elsewhere, InThreeWeeks, PharmacyId.From(Guid.CreateVersion7()));

        var matrix = (await MatrixAsync(Manager)).Value;

        matrix.Courses.Select(course => (course.CourseId, course.Title)).ShouldBe([(firstAid.Value, "erste Hilfe"), (hygiene.Value, "Hygiene")]);
        matrix.Members.Items.SelectMany(row => row.Cells).ShouldAllBe(cell => cell.CourseId != elsewhere.Value);
    }

    [Fact]
    public async Task A_course_whose_assignments_here_are_all_cancelled_is_no_column()
    {
        await TeamAsync();
        var hygiene = Course("Hygiene");
        var anna = Assigned(Anna, hygiene, InThreeWeeks);
        anna.Cancel(Manager, ChangeReason.From("Versehentlich zugewiesen"), Earlier).IsSuccess.ShouldBeTrue();
        var berta = Assigned(Berta, hygiene, InThreeWeeks);
        berta.Cancel(Manager, ChangeReason.From("Versehentlich zugewiesen"), Earlier).IsSuccess.ShouldBeTrue();

        var matrix = (await MatrixAsync(Manager)).Value;

        matrix.Courses.ShouldBeEmpty();
        matrix.Members.Items.ShouldAllBe(row => row.Cells.Count == 0);
        matrix.Assignments.ShouldBe(0);
        matrix.ComplianceRate.ShouldBeNull();
    }

    [Fact]
    public async Task Only_a_course_with_a_non_cancelled_assignment_here_is_a_column_even_if_its_learner_left()
    {
        await ComplianceMembers.StartAsync(members, Manager, Pharmacy, MembershipRole.Manager, "Mara Manager", JobFunction.Pharmacist);
        await ComplianceMembers.StartAsync(members, Anna, Pharmacy, MembershipRole.Employee, "Anna Arndt", JobFunction.Pta);
        var membership = await ComplianceMembers.StartAsync(members, Berta, Pharmacy, MembershipRole.Employee, "Berta Braun", JobFunction.Pka);
        await ComplianceMembers.EndAsync(members, membership, Berta, Pharmacy);
        var hygiene = Course("Hygiene");
        var firstAid = Course("Erste Hilfe");
        Assigned(Anna, hygiene, InThreeWeeks).Cancel(Manager, ChangeReason.From("Versehentlich zugewiesen"), Earlier).IsSuccess.ShouldBeTrue();
        Assigned(Berta, hygiene, InThreeWeeks).Cancel(Manager, ChangeReason.From("Versehentlich zugewiesen"), Earlier).IsSuccess.ShouldBeTrue();
        Assigned(Anna, firstAid, InThreeWeeks).Cancel(Manager, ChangeReason.From("Versehentlich zugewiesen"), Earlier).IsSuccess.ShouldBeTrue();
        Assigned(Berta, firstAid, InThreeWeeks);

        var matrix = (await MatrixAsync(Manager)).Value;

        matrix.Courses.ShouldHaveSingleItem().ShouldBe(new ComplianceMatrixCourseDto(firstAid.Value, "Erste Hilfe", 0, 0, 0, null));
        matrix.Members.Items.Select(row => row.PersonId).ShouldBe([Anna.Value, Manager.Value]);
        matrix.Members.Items.ShouldAllBe(row => row.Cells.Count == 0);
        matrix.Assignments.ShouldBe(0);
        matrix.ComplianceRate.ShouldBeNull();
    }

    [Fact]
    public async Task A_course_assigned_only_to_a_member_who_left_is_still_a_column()
    {
        await ComplianceMembers.StartAsync(members, Manager, Pharmacy, MembershipRole.Manager, "Mara Manager", JobFunction.Pharmacist);
        var membership = await ComplianceMembers.StartAsync(members, Berta, Pharmacy, MembershipRole.Employee, "Berta Braun", JobFunction.Pka);
        await ComplianceMembers.EndAsync(members, membership, Berta, Pharmacy);
        var hygiene = Course("Hygiene");
        Assigned(Berta, hygiene, InThreeWeeks);

        var matrix = (await MatrixAsync(Manager)).Value;

        matrix.Courses.ShouldHaveSingleItem().ShouldBe(new ComplianceMatrixCourseDto(hygiene.Value, "Hygiene", 0, 0, 0, null));
        matrix.Members.Items.ShouldHaveSingleItem().PersonId.ShouldBe(Manager.Value);
        matrix.Members.Items[0].Cells.ShouldBeEmpty();
        matrix.Assignments.ShouldBe(0);
        matrix.ComplianceRate.ShouldBeNull();
    }

    [Fact]
    public async Task Each_cell_shows_the_state_on_today_in_germany()
    {
        await TeamAsync();
        var open = Assigned(Anna, Course("A offen"), InThreeWeeks);
        var inProgress = Assigned(Anna, Course("B in Bearbeitung"), InThreeWeeks);
        inProgress.MarkStarted(Timestamp.From(new DateTimeOffset(2026, 10, 2, 8, 0, 0, TimeSpan.Zero)));
        var overdue = Assigned(Anna, Course("C überfällig"), new DateOnly(2026, 11, 22));
        var completed = Assigned(Anna, Course("D abgeschlossen"), InThreeWeeks);
        completed.Complete(CertificateId.From(Guid.CreateVersion7()), Timestamp.From(new DateTimeOffset(2026, 11, 10, 10, 0, 0, TimeSpan.Zero))).IsSuccess.ShouldBeTrue();
        var completedLate = Assigned(Anna, Course("E verspätet"), new DateOnly(2026, 11, 1));
        completedLate.Complete(CertificateId.From(Guid.CreateVersion7()), Timestamp.From(new DateTimeOffset(2026, 11, 4, 23, 30, 0, TimeSpan.Zero))).IsSuccess.ShouldBeTrue();

        var matrix = (await MatrixAsync(Manager)).Value;

        var cells = RowOf(matrix, Anna).Cells;
        cells.ShouldBe(
        [
            new ComplianceMatrixCellDto(open.Course.Value, open.Id.Value, ComplianceAssignmentState.Open, InThreeWeeks, null, null),
            new ComplianceMatrixCellDto(inProgress.Course.Value, inProgress.Id.Value, ComplianceAssignmentState.InProgress, InThreeWeeks, null, null),
            new ComplianceMatrixCellDto(overdue.Course.Value, overdue.Id.Value, ComplianceAssignmentState.Overdue, new DateOnly(2026, 11, 22), null, null),
            new ComplianceMatrixCellDto(completed.Course.Value, completed.Id.Value, ComplianceAssignmentState.Completed, InThreeWeeks, new DateOnly(2026, 11, 10), new DateOnly(2027, 11, 10)),
            new ComplianceMatrixCellDto(completedLate.Course.Value, completedLate.Id.Value, ComplianceAssignmentState.CompletedLate, new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 5), new DateOnly(2027, 11, 5)),
        ]);
        matrix.Courses.Select(course => course.CourseId).ShouldBe(cells.Select(cell => cell.CourseId));
    }

    [Theory]
    [InlineData("2026-11-20", "2026-11-20T22:59:59Z", ComplianceAssignmentState.Open)]
    [InlineData("2026-11-20", "2026-11-20T23:00:00Z", ComplianceAssignmentState.Overdue)]
    [InlineData("2026-07-01", "2026-07-01T21:59:59Z", ComplianceAssignmentState.Open)]
    [InlineData("2026-07-01", "2026-07-01T22:00:00Z", ComplianceAssignmentState.Overdue)]
    public async Task Overdue_starts_the_day_after_the_due_date_in_german_time(string dueDate, string at, ComplianceAssignmentState expected)
    {
        var due = DateOnly.Parse(dueDate, System.Globalization.CultureInfo.InvariantCulture);
        var instant = DateTimeOffset.Parse(at, System.Globalization.CultureInfo.InvariantCulture);
        await TeamAsync();
        Assigned(Anna, Course("Hygiene"), due, at: Timestamp.From(instant.AddDays(-30)));

        var matrix = (await MatrixAsync(Manager, clock: new FakeTimeProvider(instant))).Value;

        RowOf(matrix, Anna).Cells.ShouldHaveSingleItem().State.ShouldBe(expected);
    }

    [Fact]
    public async Task A_recurring_course_shows_the_follow_up_and_when_it_was_last_completed()
    {
        await TeamAsync();
        var hygiene = Course("Hygiene");
        var first = Assigned(Anna, hygiene, new DateOnly(2026, 11, 25), recurrence: RecurrenceInterval.Yearly);
        first.Complete(CertificateId.From(Guid.CreateVersion7()), Timestamp.From(new DateTimeOffset(2026, 11, 20, 23, 30, 0, TimeSpan.Zero))).IsSuccess.ShouldBeTrue();
        var followUp = first.FollowUp(AssignmentId.New(), Timestamp.From(new DateTimeOffset(2026, 11, 20, 23, 31, 0, TimeSpan.Zero))).Value;
        assignments.Stored.Add(followUp);

        var matrix = (await MatrixAsync(Manager)).Value;

        RowOf(matrix, Anna).Cells.ShouldHaveSingleItem().ShouldBe(
            new ComplianceMatrixCellDto(hygiene.Value, followUp.Id.Value, ComplianceAssignmentState.Open, new DateOnly(2027, 11, 21), new DateOnly(2026, 11, 21), null));
        matrix.Courses.ShouldHaveSingleItem().ShouldBe(new ComplianceMatrixCourseDto(hygiene.Value, "Hygiene", 1, 1, 0, 1.0));
    }

    [Fact]
    public async Task A_cancelled_assignment_newer_than_a_completed_one_leaves_the_completed_one_in_the_cell()
    {
        await TeamAsync();
        var hygiene = Course("Hygiene");
        var completed = Assigned(Anna, hygiene, InThreeWeeks);
        completed.Complete(CertificateId.From(Guid.CreateVersion7()), Timestamp.From(new DateTimeOffset(2026, 11, 10, 10, 0, 0, TimeSpan.Zero))).IsSuccess.ShouldBeTrue();
        var reassignedAt = Timestamp.From(new DateTimeOffset(2026, 11, 12, 10, 0, 0, TimeSpan.Zero));
        var cancelled = Assigned(Anna, hygiene, InThreeWeeks, at: reassignedAt);
        cancelled.Cancel(Manager, ChangeReason.From("Doppelt zugewiesen"), reassignedAt).IsSuccess.ShouldBeTrue();

        var matrix = (await MatrixAsync(Manager)).Value;

        RowOf(matrix, Anna).Cells.ShouldHaveSingleItem().ShouldBe(
            new ComplianceMatrixCellDto(hygiene.Value, completed.Id.Value, ComplianceAssignmentState.Completed, InThreeWeeks, new DateOnly(2026, 11, 10), new DateOnly(2027, 11, 10)));
    }

    [Fact]
    public async Task Rows_are_the_active_members_one_per_person_by_current_name_and_job_function_sorted_by_name()
    {
        await ComplianceMembers.StartAsync(members, Manager, Pharmacy, MembershipRole.Manager, "Mara Manager", JobFunction.Pharmacist);
        var annaMembership = await ComplianceMembers.StartAsync(members, Anna, Pharmacy, MembershipRole.Employee, "Anna Arndt", JobFunction.Pta);
        await ComplianceMembers.ChangeAsync(members, annaMembership, Anna, Pharmacy, 2, MembershipRole.Employee, "Zoe Zander", JobFunction.Pharmacist);
        var bertaMembership = await ComplianceMembers.StartAsync(members, Berta, Pharmacy, MembershipRole.Employee, "Berta Braun", JobFunction.Pka);
        await ComplianceMembers.EndAsync(members, bertaMembership, Berta, Pharmacy);
        var clara = PersonId.From(Guid.CreateVersion7());
        var ids = InOrder(Guid.CreateVersion7(), Guid.CreateVersion7());
        await ComplianceMembers.StartAsync(members, MembershipId.From(ids[1]), clara, Pharmacy, MembershipRole.Employee, "Clara Neu", JobFunction.Trainee);
        await ComplianceMembers.StartAsync(members, MembershipId.From(ids[0]), clara, Pharmacy, MembershipRole.Employee, "Clara Alt", JobFunction.Pta);
        var hygiene = Course("Hygiene");
        Assigned(Anna, hygiene, InThreeWeeks);
        Assigned(Berta, hygiene, LastFriday);

        var matrix = (await MatrixAsync(Manager)).Value;

        matrix.Members.Items.Select(row => (row.PersonId, row.Name, row.JobFunction)).ShouldBe(
        [
            (clara.Value, "Clara Neu", ComplianceJobFunction.Trainee),
            (Manager.Value, "Mara Manager", ComplianceJobFunction.Pharmacist),
            (Anna.Value, "Zoe Zander", ComplianceJobFunction.Pharmacist),
        ]);
        matrix.Members.TotalCount.ShouldBe(3);
        matrix.Assignments.ShouldBe(1);
        matrix.CompliantAssignments.ShouldBe(1);
        matrix.OverdueAssignments.ShouldBe(0);
        matrix.ComplianceRate.ShouldBe(1.0);
        matrix.Courses.ShouldHaveSingleItem().ShouldBe(new ComplianceMatrixCourseDto(hygiene.Value, "Hygiene", 1, 1, 0, 1.0));
    }

    [Fact]
    public async Task The_rate_counts_open_and_still_valid_completed_cells_as_compliant_and_overdue_or_expired_ones_not()
    {
        await TeamAsync();
        var hygiene = Course("Hygiene");
        var firstAid = Course("Erste Hilfe");
        Assigned(Anna, hygiene, InThreeWeeks);
        Assigned(Berta, hygiene, LastFriday);
        var completed = Assigned(Manager, hygiene, InThreeWeeks);
        completed.Complete(CertificateId.From(Guid.CreateVersion7()), Timestamp.From(new DateTimeOffset(2026, 11, 10, 10, 0, 0, TimeSpan.Zero))).IsSuccess.ShouldBeTrue();
        var expired = Assigned(Anna, firstAid, new DateOnly(2025, 10, 15), at: Timestamp.From(new DateTimeOffset(2025, 9, 1, 8, 0, 0, TimeSpan.Zero)));
        expired.Complete(CertificateId.From(Guid.CreateVersion7()), Timestamp.From(new DateTimeOffset(2025, 10, 1, 10, 0, 0, TimeSpan.Zero))).IsSuccess.ShouldBeTrue();

        var matrix = (await MatrixAsync(Manager)).Value;

        RowOf(matrix, Anna).Cells.First(cell => cell.CourseId == firstAid.Value).ShouldBe(
            new ComplianceMatrixCellDto(firstAid.Value, expired.Id.Value, ComplianceAssignmentState.Completed, new DateOnly(2025, 10, 15), new DateOnly(2025, 10, 1), new DateOnly(2026, 10, 1)));
        matrix.Courses.ShouldBe(
        [
            new ComplianceMatrixCourseDto(firstAid.Value, "Erste Hilfe", 1, 0, 0, 0.0),
            new ComplianceMatrixCourseDto(hygiene.Value, "Hygiene", 3, 2, 1, 2d / 3d),
        ]);
        matrix.Assignments.ShouldBe(4);
        matrix.CompliantAssignments.ShouldBe(2);
        matrix.OverdueAssignments.ShouldBe(1);
        matrix.ComplianceRate.ShouldBe(0.5);
    }

    [Theory]
    [InlineData("2025-11-23T22:30:00Z", "2026-11-23T22:59:59Z", "2026-11-23", 1.0)]
    [InlineData("2025-11-23T22:30:00Z", "2026-11-23T23:00:00Z", "2026-11-23", 0.0)]
    [InlineData("2025-11-23T23:00:00Z", "2026-11-24T22:59:59Z", "2026-11-24", 1.0)]
    [InlineData("2025-11-23T23:00:00Z", "2026-11-24T23:00:00Z", "2026-11-24", 0.0)]
    public async Task A_completion_is_compliant_through_its_last_valid_day_in_german_time(string completedAt, string at, string validUntil, double expectedRate)
    {
        var instant = DateTimeOffset.Parse(at, System.Globalization.CultureInfo.InvariantCulture);
        await TeamAsync();
        var hygiene = Course("Hygiene");
        var completed = Assigned(Anna, hygiene, new DateOnly(2025, 12, 31), at: Timestamp.From(new DateTimeOffset(2025, 11, 1, 8, 0, 0, TimeSpan.Zero)));
        completed.Complete(CertificateId.From(Guid.CreateVersion7()), Timestamp.From(DateTimeOffset.Parse(completedAt, System.Globalization.CultureInfo.InvariantCulture))).IsSuccess.ShouldBeTrue();

        var matrix = (await MatrixAsync(Manager, clock: new FakeTimeProvider(instant))).Value;

        var cell = RowOf(matrix, Anna).Cells.ShouldHaveSingleItem();
        cell.State.ShouldBe(ComplianceAssignmentState.Completed);
        cell.ValidUntil.ShouldBe(DateOnly.Parse(validUntil, System.Globalization.CultureInfo.InvariantCulture));
        matrix.ComplianceRate.ShouldBe(expectedRate);
        matrix.Courses.ShouldHaveSingleItem().ComplianceRate.ShouldBe(expectedRate);
    }

    [Fact]
    public async Task A_completion_is_valid_for_the_months_of_the_course_s_latest_version()
    {
        await TeamAsync();
        var hygiene = Course("Hygiene", 24);
        courses.Versions.Add(CourseVersion.Published(CourseVersionId.From(Guid.CreateVersion7()), hygiene, VersionNumber.From(2), CourseTitle.From("Hygiene"), ValidityMonths.From(6)));
        var completed = Assigned(Anna, hygiene, new DateOnly(2026, 4, 30), at: Timestamp.From(new DateTimeOffset(2026, 3, 1, 8, 0, 0, TimeSpan.Zero)));
        completed.Complete(CertificateId.From(Guid.CreateVersion7()), Timestamp.From(new DateTimeOffset(2026, 4, 1, 10, 0, 0, TimeSpan.Zero))).IsSuccess.ShouldBeTrue();

        var matrix = (await MatrixAsync(Manager)).Value;

        RowOf(matrix, Anna).Cells.ShouldHaveSingleItem().ValidUntil.ShouldBe(new DateOnly(2026, 10, 1));
        (matrix.Assignments, matrix.CompliantAssignments, matrix.OverdueAssignments, matrix.ComplianceRate).ShouldBe((1, 0, 0, 0.0));
    }

    /// <summary>#287: a completion is valid for the months of the version its certificate was issued for, not the latest's.</summary>
    [Fact]
    public async Task A_completion_is_valid_for_the_months_of_the_version_it_was_certified_for()
    {
        await TeamAsync();
        var hygiene = Course("Hygiene", 24);
        var first = courses.Versions.Single(version => version.Course == hygiene).Id;
        courses.Versions.Add(CourseVersion.Published(CourseVersionId.From(Guid.CreateVersion7()), hygiene, VersionNumber.From(2), CourseTitle.From("Hygiene"), ValidityMonths.From(6)));
        CompletedOn(Anna, hygiene, first);

        var matrix = (await MatrixAsync(Manager)).Value;

        RowOf(matrix, Anna).Cells.ShouldHaveSingleItem().ValidUntil.ShouldBe(new DateOnly(2028, 4, 1));
        (matrix.Assignments, matrix.CompliantAssignments, matrix.OverdueAssignments, matrix.ComplianceRate).ShouldBe((1, 1, 0, 1.0));
    }

    [Fact]
    public async Task A_completion_certified_on_a_shorter_version_runs_out_although_the_latest_is_valid_longer()
    {
        await TeamAsync();
        var hygiene = Course("Hygiene", 6);
        var first = courses.Versions.Single(version => version.Course == hygiene).Id;
        courses.Versions.Add(CourseVersion.Published(CourseVersionId.From(Guid.CreateVersion7()), hygiene, VersionNumber.From(2), CourseTitle.From("Hygiene"), ValidityMonths.From(24)));
        CompletedOn(Anna, hygiene, first);

        var matrix = (await MatrixAsync(Manager)).Value;

        RowOf(matrix, Anna).Cells.ShouldHaveSingleItem().ValidUntil.ShouldBe(new DateOnly(2026, 10, 1));
        (matrix.Assignments, matrix.CompliantAssignments, matrix.OverdueAssignments, matrix.ComplianceRate).ShouldBe((1, 0, 0, 0.0));
    }

    [Fact]
    public async Task A_certificate_for_a_version_Compliance_does_not_know_falls_back_to_the_latest_version()
    {
        await TeamAsync();
        var hygiene = Course("Hygiene", 24);
        courses.Versions.Add(CourseVersion.Published(CourseVersionId.From(Guid.CreateVersion7()), hygiene, VersionNumber.From(2), CourseTitle.From("Hygiene"), ValidityMonths.From(6)));
        CompletedOn(Anna, hygiene, CourseVersionId.From(Guid.CreateVersion7()));

        var matrix = (await MatrixAsync(Manager)).Value;

        RowOf(matrix, Anna).Cells.ShouldHaveSingleItem().ValidUntil.ShouldBe(new DateOnly(2026, 10, 1));
    }

    [Fact]
    public async Task A_fully_trained_team_is_entirely_compliant()
    {
        await TeamAsync();
        var hygiene = Course("Hygiene");
        PersonId[] learners = [Anna, Berta, Manager];
        foreach (var learner in learners)
        {
            Assigned(learner, hygiene, InThreeWeeks)
                .Complete(CertificateId.From(Guid.CreateVersion7()), Timestamp.From(new DateTimeOffset(2026, 11, 10, 10, 0, 0, TimeSpan.Zero)))
                .IsSuccess.ShouldBeTrue();
        }

        var matrix = (await MatrixAsync(Manager)).Value;

        matrix.Courses.ShouldHaveSingleItem().ShouldBe(new ComplianceMatrixCourseDto(hygiene.Value, "Hygiene", 3, 3, 0, 1.0));
        (matrix.Assignments, matrix.CompliantAssignments, matrix.OverdueAssignments, matrix.ComplianceRate).ShouldBe((3, 3, 0, 1.0));
    }

    [Fact]
    public async Task Each_column_has_its_own_tally_over_all_rows()
    {
        await TeamAsync();
        var hygiene = Course("Hygiene");
        var firstAid = Course("Erste Hilfe");
        Assigned(Anna, hygiene, InThreeWeeks);
        Assigned(Berta, hygiene, LastFriday);
        var started = Assigned(Anna, firstAid, InThreeWeeks);
        started.MarkStarted(Timestamp.From(new DateTimeOffset(2026, 10, 2, 8, 0, 0, TimeSpan.Zero)));
        var completed = Assigned(Berta, firstAid, InThreeWeeks);
        completed.Complete(CertificateId.From(Guid.CreateVersion7()), Timestamp.From(new DateTimeOffset(2026, 11, 10, 10, 0, 0, TimeSpan.Zero))).IsSuccess.ShouldBeTrue();

        var matrix = (await MatrixAsync(Manager)).Value;

        matrix.Courses.ShouldBe(
        [
            new ComplianceMatrixCourseDto(firstAid.Value, "Erste Hilfe", 2, 2, 0, 1.0),
            new ComplianceMatrixCourseDto(hygiene.Value, "Hygiene", 2, 1, 1, 0.5),
        ]);
        matrix.Assignments.ShouldBe(4);
        matrix.CompliantAssignments.ShouldBe(3);
        matrix.OverdueAssignments.ShouldBe(1);
        matrix.ComplianceRate.ShouldBe(0.75);
    }

    [Fact]
    public async Task The_job_function_filter_narrows_rows_and_every_rate_but_keeps_the_columns()
    {
        await TeamAsync();
        var hygiene = Course("Hygiene");
        var firstAid = Course("Erste Hilfe");
        Assigned(Anna, hygiene, LastFriday);
        Assigned(Berta, hygiene, InThreeWeeks);
        Assigned(Berta, firstAid, InThreeWeeks);

        var matrix = (await MatrixAsync(Manager, JobFunction.Pta)).Value;

        matrix.Members.Items.ShouldHaveSingleItem().PersonId.ShouldBe(Anna.Value);
        matrix.Members.TotalCount.ShouldBe(1);
        matrix.Courses.ShouldBe(
        [
            new ComplianceMatrixCourseDto(firstAid.Value, "Erste Hilfe", 0, 0, 0, null),
            new ComplianceMatrixCourseDto(hygiene.Value, "Hygiene", 1, 0, 1, 0.0),
        ]);
        matrix.Assignments.ShouldBe(1);
        matrix.CompliantAssignments.ShouldBe(0);
        matrix.OverdueAssignments.ShouldBe(1);
        matrix.ComplianceRate.ShouldBe(0.0);
    }

    [Fact]
    public async Task Rows_are_paged_while_the_columns_and_rates_cover_the_whole_pharmacy()
    {
        await TeamAsync();
        var hygiene = Course("Hygiene");
        var firstAid = Course("Erste Hilfe");
        Assigned(Anna, hygiene, LastFriday);
        Assigned(Berta, hygiene, InThreeWeeks);
        Assigned(Manager, firstAid, InThreeWeeks);

        var first = (await MatrixAsync(Manager, page: PageOf(1), pageSize: PageSizeOf(2))).Value;
        var second = (await MatrixAsync(Manager, page: PageOf(2), pageSize: PageSizeOf(2))).Value;

        first.Members.Items.Select(row => row.PersonId).ShouldBe([Anna.Value, Berta.Value]);
        second.Members.Items.ShouldHaveSingleItem().PersonId.ShouldBe(Manager.Value);
        second.Members.TotalCount.ShouldBe(3);
        second.Members.Page.ShouldBe(2);
        second.Members.PageSize.ShouldBe(2);
        second.Courses.ShouldBe(first.Courses);
        second.Courses.ShouldBe(
        [
            new ComplianceMatrixCourseDto(firstAid.Value, "Erste Hilfe", 1, 1, 0, 1.0),
            new ComplianceMatrixCourseDto(hygiene.Value, "Hygiene", 2, 1, 1, 0.5),
        ]);
        (second.Assignments, second.CompliantAssignments, second.OverdueAssignments, second.ComplianceRate).ShouldBe((3, 2, 1, 2d / 3d));
        (first.Assignments, first.CompliantAssignments, first.OverdueAssignments, first.ComplianceRate).ShouldBe((3, 2, 1, 2d / 3d));
    }

    [Fact]
    public async Task A_course_without_a_stored_version_is_left_out()
    {
        await TeamAsync();
        var hygiene = Course("Hygiene");
        Assigned(Anna, hygiene, InThreeWeeks);
        Assigned(Anna, CourseId.From(Guid.CreateVersion7()), LastFriday);

        var matrix = (await MatrixAsync(Manager)).Value;

        matrix.Courses.ShouldHaveSingleItem().ShouldBe(new ComplianceMatrixCourseDto(hygiene.Value, "Hygiene", 1, 1, 0, 1.0));
        RowOf(matrix, Anna).Cells.ShouldHaveSingleItem().CourseId.ShouldBe(hygiene.Value);
        (matrix.Assignments, matrix.CompliantAssignments, matrix.OverdueAssignments, matrix.ComplianceRate).ShouldBe((1, 1, 0, 1.0));
    }

    private static ComplianceMatrixRowDto RowOf(ComplianceMatrixDto matrix, PersonId person) =>
        matrix.Members.Items.Single(row => row.PersonId == person.Value);

    private static Page PageOf(int value) =>
        Page.TryFrom(value, out var page) ? page : throw new InvalidOperationException($"{value} is not a page.");

    private static PageSize PageSizeOf(int value) =>
        PageSize.TryFrom(value, out var pageSize) ? pageSize : throw new InvalidOperationException($"{value} is not a page size.");

    // Guid v7 orders by the millisecond only; two made in the same millisecond come out in random order.
    private static Guid[] InOrder(Guid one, Guid other)
    {
        Guid[] ids = [one, other];
        Array.Sort(ids);
        return ids;
    }

    /// <summary>The pharmacy's team: Mara the manager (Pharmacist), Anna (Pta) and Berta (Pka) as employees.</summary>
    private async Task TeamAsync()
    {
        await ComplianceMembers.StartAsync(members, Manager, Pharmacy, MembershipRole.Manager, "Mara Manager", JobFunction.Pharmacist);
        await ComplianceMembers.StartAsync(members, Anna, Pharmacy, MembershipRole.Employee, "Anna Arndt", JobFunction.Pta);
        await ComplianceMembers.StartAsync(members, Berta, Pharmacy, MembershipRole.Employee, "Berta Braun", JobFunction.Pka);
    }

    /// <summary>A course Compliance holds version 1 of, under the title, valid for the months given (12 unless said).</summary>
    private CourseId Course(string title, int validityMonths = 12)
    {
        var course = CourseId.From(Guid.CreateVersion7());
        courses.Versions.Add(CourseVersion.Published(CourseVersionId.From(Guid.CreateVersion7()), course, VersionNumber.From(1), CourseTitle.From(title), ValidityMonths.From(validityMonths)));
        return course;
    }

    /// <summary>
    /// The learner's assignment of the course, completed on 2026-04-01 by a certificate Compliance kept a copy of, issued
    /// for the version given.
    /// </summary>
    private void CompletedOn(PersonId learner, CourseId course, CourseVersionId version)
    {
        var completedAt = Timestamp.From(new DateTimeOffset(2026, 4, 1, 10, 0, 0, TimeSpan.Zero));
        var certificate = CertificateId.From(Guid.CreateVersion7());
        var assignment = Assigned(learner, course, new DateOnly(2026, 4, 30), at: Timestamp.From(new DateTimeOffset(2026, 3, 1, 8, 0, 0, TimeSpan.Zero)));
        assignment.Complete(certificate, completedAt).IsSuccess.ShouldBeTrue();
        progress.Add(IssuedCertificate.Of(certificate, learner, course, version, completedAt));
    }

    /// <summary>An assignment of the course to the learner, stored as it is assigned; the caller moves it on.</summary>
    private Assignment Assigned(PersonId learner, CourseId course, DateOnly dueDate, PharmacyId? pharmacy = null, Timestamp? at = null, RecurrenceInterval? recurrence = null)
    {
        var assignment = Assignment.Assign(AssignmentId.New(), learner, pharmacy ?? Pharmacy, course, DueDate.From(dueDate), recurrence, Manager, at ?? Earlier).Value;
        assignments.Stored.Add(assignment);
        return assignment;
    }

    private Task<Result<ComplianceMatrixDto, ApiError>> MatrixAsync(
        PersonId by,
        JobFunction? jobFunction = null,
        Page? page = null,
        PageSize? pageSize = null,
        TimeProvider? clock = null) =>
        new GetComplianceMatrixHandler(members, assignments, courses, progress, clock ?? time).HandleAsync(
            new GetComplianceMatrix(Pharmacy, by, jobFunction, page ?? Page.First, pageSize ?? PageSize.Default),
            TestContext.Current.CancellationToken);
}
