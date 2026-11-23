using System.Net;
using Microsoft.Extensions.Time.Testing;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.DTOs;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.Queries.ListMyAssignments;
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
/// The learner's dashboard (#35): the caller's own open and in-progress assignments in the selected pharmacy, overdue
/// first, then by due date, title and assignment id, each with its state today in German time, its course's latest title
/// and the course version to open: the running session's to continue, else the current one to begin, else none
/// (ADR 0024). Any member of the pharmacy is served, whatever their role; anyone else is told it does not exist
/// (docs/adr/0003, docs/adr/0015).
/// </summary>
public class ListMyAssignmentsTests
{
    // Monday 2026-11-23, 10:00 in Berlin.
    private static readonly DateTimeOffset Now = new(2026, 11, 23, 9, 0, 0, TimeSpan.Zero);
    private static readonly Timestamp Earlier = Timestamp.From(new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero));
    private static readonly DateOnly InThreeWeeks = new(2026, 12, 15);
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
    public async Task The_caller_sees_only_their_own_active_assignments_in_that_pharmacy()
    {
        var otherPharmacy = PharmacyId.From(Guid.CreateVersion7());
        await ComplianceMembers.StartAsync(members, Anna, Pharmacy, MembershipRole.Employee);
        await ComplianceMembers.StartAsync(members, Berta, Pharmacy, MembershipRole.Employee);
        await ComplianceMembers.StartAsync(members, Anna, otherPharmacy, MembershipRole.Employee);
        var hygiene = Course("Hygiene");
        var firstAid = Course("Erste Hilfe");
        var privacy = Course("Datenschutz");
        var mine = Assigned(Anna, hygiene.Course, InThreeWeeks);
        Assigned(Berta, hygiene.Course, InThreeWeeks);
        Assigned(Anna, firstAid.Course, InThreeWeeks, otherPharmacy);
        var completed = Assigned(Anna, firstAid.Course, InThreeWeeks);
        completed.Complete(CertificateId.From(Guid.CreateVersion7()), Earlier).IsSuccess.ShouldBeTrue();
        var cancelled = Assigned(Anna, privacy.Course, InThreeWeeks);
        cancelled.Cancel(Manager, ChangeReason.From("Versehentlich zugewiesen"), Earlier).IsSuccess.ShouldBeTrue();

        var result = await ListAsync(Anna);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Items.ShouldBe(
        [
            new ComplianceMyAssignmentDto(mine.Id.Value, hygiene.Course.Value, "Hygiene", ComplianceAssignmentState.Open, InThreeWeeks, hygiene.Version.Value, false),
        ]);
        result.Value.TotalCount.ShouldBe(1);
        result.Value.Page.ShouldBe(1);
        result.Value.PageSize.ShouldBe(PageSize.Default.Value);
    }

    [Fact]
    public async Task Overdue_comes_first_then_by_due_date_then_by_title_ignoring_case_then_by_assignment_id()
    {
        await ComplianceMembers.StartAsync(members, Anna, Pharmacy, MembershipRole.Employee);
        var sameTitleOne = Assigned(Anna, Course("Gleich").Course, InThreeWeeks);
        var beta = Assigned(Anna, Course("Beta").Course, InThreeWeeks);
        var early = Assigned(Anna, Course("Zuerst fällig").Course, new DateOnly(2026, 12, 1));
        var overdueLater = Assigned(Anna, Course("Anfang").Course, new DateOnly(2026, 11, 21));
        var sameTitleOther = Assigned(Anna, Course("Gleich").Course, InThreeWeeks);
        var alpha = Assigned(Anna, Course("alpha").Course, InThreeWeeks);
        var overdueFirst = Assigned(Anna, Course("Zebra").Course, new DateOnly(2026, 11, 20));
        Guid[] sameTitle = [.. new[] { sameTitleOne.Id.Value, sameTitleOther.Id.Value }.Order()];

        var result = await ListAsync(Anna);

        result.Value.Items.Select(row => row.AssignmentId).ShouldBe(
        [
            overdueFirst.Id.Value,
            overdueLater.Id.Value,
            early.Id.Value,
            alpha.Id.Value,
            beta.Id.Value,
            sameTitle[0],
            sameTitle[1],
        ]);
        result.Value.Items.Select(row => row.State).ShouldBe(
        [
            ComplianceAssignmentState.Overdue,
            ComplianceAssignmentState.Overdue,
            ComplianceAssignmentState.Open,
            ComplianceAssignmentState.Open,
            ComplianceAssignmentState.Open,
            ComplianceAssignmentState.Open,
            ComplianceAssignmentState.Open,
        ]);
        result.Value.TotalCount.ShouldBe(7);
    }

    [Fact]
    public async Task Due_today_is_not_overdue_and_due_yesterday_in_German_time_is()
    {
        // 2026-11-22 23:30 UTC is already 2026-11-23 00:30 in Berlin.
        var justAfterMidnight = new FakeTimeProvider(new DateTimeOffset(2026, 11, 22, 23, 30, 0, TimeSpan.Zero));
        await ComplianceMembers.StartAsync(members, Anna, Pharmacy, MembershipRole.Employee);
        var dueToday = Assigned(Anna, Course("Heute fällig").Course, new DateOnly(2026, 11, 23));
        var dueYesterday = Assigned(Anna, Course("Gestern fällig").Course, new DateOnly(2026, 11, 22));

        var result = await ListAsync(Anna, clock: justAfterMidnight);

        result.Value.Items.Select(row => (row.AssignmentId, row.State)).ShouldBe(
        [
            (dueYesterday.Id.Value, ComplianceAssignmentState.Overdue),
            (dueToday.Id.Value, ComplianceAssignmentState.Open),
        ]);
    }

    [Fact]
    public async Task An_assignment_in_progress_or_locked_shows_so_and_overdue_wins_over_locked()
    {
        await ComplianceMembers.StartAsync(members, Anna, Pharmacy, MembershipRole.Employee);
        var inProgress = Assigned(Anna, Course("In Arbeit").Course, InThreeWeeks);
        inProgress.MarkStarted(Earlier);
        var locked = Assigned(Anna, Course("Gesperrt").Course, InThreeWeeks);
        locked.MarkStarted(Earlier);
        locked.MarkLocked(Earlier);
        var lockedAndOverdue = Assigned(Anna, Course("Gesperrt und überfällig").Course, new DateOnly(2026, 11, 20));
        lockedAndOverdue.MarkStarted(Earlier);
        lockedAndOverdue.MarkLocked(Earlier);

        var result = await ListAsync(Anna);

        result.Value.Items.Select(row => (row.AssignmentId, row.State)).ShouldBe(
        [
            (lockedAndOverdue.Id.Value, ComplianceAssignmentState.Overdue),
            (locked.Id.Value, ComplianceAssignmentState.Locked),
            (inProgress.Id.Value, ComplianceAssignmentState.InProgress),
        ]);
    }

    [Fact]
    public async Task A_running_session_gives_its_own_version_to_continue_although_a_newer_one_is_current()
    {
        await ComplianceMembers.StartAsync(members, Anna, Pharmacy, MembershipRole.Employee);
        var hygiene = Course("Hygiene");
        Version(hygiene.Course, 2, "Hygiene");
        var assignment = Assigned(Anna, hygiene.Course, InThreeWeeks);
        assignment.MarkStarted(Timestamp.From(Earlier.Value.AddDays(1)));
        Started(Anna, hygiene.Course, hygiene.Version, Earlier.Value.AddDays(1));

        var row = (await ListAsync(Anna)).Value.Items.ShouldHaveSingleItem();

        row.StartCourseVersionId.ShouldBe(hygiene.Version.Value);
        row.ContinuesSession.ShouldBeTrue();
        row.State.ShouldBe(ComplianceAssignmentState.InProgress);
    }

    [Fact]
    public async Task Without_a_running_session_the_current_version_is_begun()
    {
        await ComplianceMembers.StartAsync(members, Anna, Pharmacy, MembershipRole.Employee);
        var hygiene = Course("Hygiene");
        var current = Version(hygiene.Course, 2, "Hygiene");
        Assigned(Anna, hygiene.Course, InThreeWeeks);

        var row = (await ListAsync(Anna)).Value.Items.ShouldHaveSingleItem();

        row.StartCourseVersionId.ShouldBe(current.Value);
        row.ContinuesSession.ShouldBeFalse();
    }

    [Fact]
    public async Task Without_a_running_session_and_with_the_latest_version_retired_there_is_nothing_to_start()
    {
        await ComplianceMembers.StartAsync(members, Anna, Pharmacy, MembershipRole.Employee);
        var hygiene = Course("Hygiene");
        courses.Retirements.Add(CourseVersionRetirement.Of(hygiene.Version, hygiene.Course));
        Assigned(Anna, hygiene.Course, InThreeWeeks);

        var row = (await ListAsync(Anna)).Value.Items.ShouldHaveSingleItem();

        row.StartCourseVersionId.ShouldBeNull();
        row.ContinuesSession.ShouldBeFalse();
        row.CourseTitle.ShouldBe("Hygiene");
    }

    [Fact]
    public async Task A_session_ended_by_a_certificate_leaves_the_current_version_to_begin()
    {
        await ComplianceMembers.StartAsync(members, Anna, Pharmacy, MembershipRole.Employee);
        var hygiene = Course("Hygiene");
        var current = Version(hygiene.Course, 2, "Hygiene");
        Started(Anna, hygiene.Course, hygiene.Version, Earlier.Value.AddDays(-10));
        progress.Certificates.Add(IssuedCertificate.Of(CertificateId.From(Guid.CreateVersion7()), Anna, hygiene.Course, hygiene.Version, Timestamp.From(Earlier.Value.AddDays(-9))));
        Assigned(Anna, hygiene.Course, InThreeWeeks);

        var row = (await ListAsync(Anna)).Value.Items.ShouldHaveSingleItem();

        row.StartCourseVersionId.ShouldBe(current.Value);
        row.ContinuesSession.ShouldBeFalse();
    }

    [Fact]
    public async Task A_session_started_before_the_assignment_counts()
    {
        await ComplianceMembers.StartAsync(members, Anna, Pharmacy, MembershipRole.Employee);
        var hygiene = Course("Hygiene");
        Started(Anna, hygiene.Course, hygiene.Version, Earlier.Value.AddDays(-3));
        Version(hygiene.Course, 2, "Hygiene");
        Assigned(Anna, hygiene.Course, InThreeWeeks);

        var row = (await ListAsync(Anna)).Value.Items.ShouldHaveSingleItem();

        row.StartCourseVersionId.ShouldBe(hygiene.Version.Value);
        row.ContinuesSession.ShouldBeTrue();
        row.State.ShouldBe(ComplianceAssignmentState.Open);
    }

    [Fact]
    public async Task Sessions_of_another_person_or_another_course_are_ignored()
    {
        await ComplianceMembers.StartAsync(members, Anna, Pharmacy, MembershipRole.Employee);
        var hygiene = Course("Hygiene");
        var current = Version(hygiene.Course, 2, "Hygiene");
        var firstAid = Course("Erste Hilfe");
        Started(Berta, hygiene.Course, hygiene.Version, Earlier.Value.AddDays(1));
        Started(Anna, firstAid.Course, firstAid.Version, Earlier.Value.AddDays(1));
        var assignment = Assigned(Anna, hygiene.Course, InThreeWeeks);

        var row = (await ListAsync(Anna)).Value.Items.ShouldHaveSingleItem();

        row.AssignmentId.ShouldBe(assignment.Id.Value);
        row.StartCourseVersionId.ShouldBe(current.Value);
        row.ContinuesSession.ShouldBeFalse();
    }

    [Fact]
    public async Task The_title_is_the_latest_versions()
    {
        await ComplianceMembers.StartAsync(members, Anna, Pharmacy, MembershipRole.Employee);
        var hygiene = Course("Hygiene alt");
        Version(hygiene.Course, 2, "Hygiene neu");
        Assigned(Anna, hygiene.Course, InThreeWeeks);

        var row = (await ListAsync(Anna)).Value.Items.ShouldHaveSingleItem();

        row.CourseTitle.ShouldBe("Hygiene neu");
        row.CourseId.ShouldBe(hygiene.Course.Value);
    }

    [Fact]
    public async Task An_employee_without_any_capability_is_served()
    {
        await ComplianceMembers.StartAsync(members, Berta, Pharmacy, MembershipRole.Employee);
        var assignment = Assigned(Berta, Course("Hygiene").Course, InThreeWeeks);

        var result = await ListAsync(Berta);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Items.ShouldHaveSingleItem().AssignmentId.ShouldBe(assignment.Id.Value);
    }

    [Fact]
    public async Task A_non_member_is_told_the_pharmacy_does_not_exist()
    {
        await ComplianceMembers.StartAsync(members, Anna, PharmacyId.From(Guid.CreateVersion7()), MembershipRole.Employee);
        Assigned(Anna, Course("Hygiene").Course, InThreeWeeks);

        var result = await ListAsync(Anna);

        result.IsSuccess.ShouldBeFalse();
        result.Error.Code.ShouldBe("compliance.pharmacy-not-found");
        result.Error.Status.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_member_whose_membership_ended_is_told_the_pharmacy_does_not_exist()
    {
        var membership = await ComplianceMembers.StartAsync(members, Anna, Pharmacy, MembershipRole.Employee);
        await ComplianceMembers.EndAsync(members, membership, Anna, Pharmacy);
        Assigned(Anna, Course("Hygiene").Course, InThreeWeeks);

        var result = await ListAsync(Anna);

        result.IsSuccess.ShouldBeFalse();
        result.Error.Code.ShouldBe("compliance.pharmacy-not-found");
        result.Error.Status.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Page_two_of_size_one_holds_the_second_row_and_counts_every_row()
    {
        await ComplianceMembers.StartAsync(members, Anna, Pharmacy, MembershipRole.Employee);
        Assigned(Anna, Course("Hygiene").Course, new DateOnly(2026, 12, 1));
        var second = Assigned(Anna, Course("Erste Hilfe").Course, InThreeWeeks);

        var result = await ListAsync(Anna, PageOf(2), PageSizeOf(1));

        result.Value.Items.ShouldHaveSingleItem().AssignmentId.ShouldBe(second.Id.Value);
        result.Value.TotalCount.ShouldBe(2);
        result.Value.Page.ShouldBe(2);
        result.Value.PageSize.ShouldBe(1);
    }

    [Fact]
    public async Task An_active_assignment_whose_course_Compliance_does_not_hold_is_a_bug()
    {
        await ComplianceMembers.StartAsync(members, Anna, Pharmacy, MembershipRole.Employee);
        var unknown = CourseId.From(Guid.CreateVersion7());
        var assignment = Assigned(Anna, unknown, InThreeWeeks);

        var thrown = await Should.ThrowAsync<InvalidOperationException>(() => ListAsync(Anna));

        thrown.Message.ShouldContain(assignment.Id.ToString());
        thrown.Message.ShouldContain(unknown.ToString());
    }

    private static Page PageOf(int value) =>
        Page.TryFrom(value, out var page) ? page : throw new InvalidOperationException($"{value} is not a page.");

    private static PageSize PageSizeOf(int value) =>
        PageSize.TryFrom(value, out var pageSize) ? pageSize : throw new InvalidOperationException($"{value} is not a page size.");

    /// <summary>A course Compliance holds version 1 of, under the title; answers the course and that version.</summary>
    private (CourseId Course, CourseVersionId Version) Course(string title)
    {
        var course = CourseId.From(Guid.CreateVersion7());
        return (course, Version(course, 1, title));
    }

    /// <summary>A further version of the course Compliance holds, with its number and title.</summary>
    private CourseVersionId Version(CourseId course, int number, string title)
    {
        var version = CourseVersionId.From(Guid.CreateVersion7());
        courses.Versions.Add(CourseVersion.Published(version, course, VersionNumber.From(number), CourseTitle.From(title), ValidityMonths.From(24)));
        return version;
    }

    /// <summary>An assignment of the course to the learner, assigned earlier and stored; the caller moves it on.</summary>
    private Assignment Assigned(PersonId learner, CourseId course, DateOnly dueDate, PharmacyId? pharmacy = null)
    {
        var assignment = Assignment.Assign(AssignmentId.New(), learner, pharmacy ?? Pharmacy, course, DueDate.From(dueDate), null, Manager, Earlier).Value;
        assignments.Stored.Add(assignment);
        return assignment;
    }

    /// <summary>Compliance's copy of a session Learning started for the learner on that version of the course.</summary>
    private void Started(PersonId learner, CourseId course, CourseVersionId version, DateTimeOffset at) =>
        progress.Starts.Add(LearningSessionStart.Of(LearningSessionId.From(Guid.CreateVersion7()), learner, course, version, Timestamp.From(at)));

    private Task<Result<PagedResult<ComplianceMyAssignmentDto>, ApiError>> ListAsync(PersonId learner, Page? page = null, PageSize? pageSize = null, TimeProvider? clock = null) =>
        new ListMyAssignmentsHandler(members, assignments, courses, progress, clock ?? time).HandleAsync(
            new ListMyAssignments(learner, Pharmacy, page ?? Page.First, pageSize ?? PageSize.Default),
            TestContext.Current.CancellationToken);
}
