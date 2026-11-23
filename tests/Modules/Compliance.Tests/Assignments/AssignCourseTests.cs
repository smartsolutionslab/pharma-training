using System.Net;
using Microsoft.Extensions.Time.Testing;
using SmartSolutionsLab.PharmaTraining.Contracts.Compliance;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.Commands.AssignCourse;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.DTOs;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Assignments;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Courses;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Members;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Tests.Courses;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Tests.Members;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Tests.Assignments;

/// <summary>
/// A manager assigns a course to several members of their pharmacy at once (D8): each learner is assigned or refused by
/// name, and what is assigned is saved and announced in one commit. Members and courses are seeded through their
/// stores and committed before the act; the assignment store is the unit of work the handler commits.
/// </summary>
public class AssignCourseTests
{
    private static readonly DateTimeOffset Monday = new(2027, 3, 1, 9, 0, 0, TimeSpan.FromHours(1));
    private static readonly PharmacyId Pharmacy = PharmacyId.From(Guid.CreateVersion7());
    private static readonly CourseId Hygiene = CourseId.From(Guid.CreateVersion7());
    private static readonly PersonId Manager = PersonId.From(Guid.CreateVersion7());
    private static readonly PersonId Anna = PersonId.From(Guid.CreateVersion7());
    private static readonly PersonId Ben = PersonId.From(Guid.CreateVersion7());
    private static readonly DueDate EndOfMarch = DueDate.From(new DateOnly(2027, 3, 31));

    private readonly FakeTimeProvider time = new(Monday);
    private readonly InMemoryMemberStore members = new();
    private readonly InMemoryCourseVersionStore courses = new();
    private readonly InMemoryAssignmentStore assignments = new();

    [Fact]
    public async Task A_manager_assigns_a_course_to_several_members_and_each_is_announced()
    {
        await SeedAsync();

        var result = await AssignAsync([Anna, Ben]);

        var answer = result.Value;
        answer.Refused.ShouldBeEmpty();
        answer.Assigned.Select(learner => learner.PersonId).ShouldBe([Anna.Value, Ben.Value]);
        answer.Assigned.Select(learner => learner.AssignmentId).Distinct().Count().ShouldBe(2);
        assignments.Stored.Count.ShouldBe(2);
        foreach (var stored in assignments.Stored)
        {
            stored.Status.ShouldBe(AssignmentStatus.Open);
            stored.Pharmacy.ShouldBe(Pharmacy);
            stored.Course.ShouldBe(Hygiene);
            stored.DueDate.ShouldBe(EndOfMarch);
            stored.Recurrence.ShouldBeNull();
            stored.AssignedBy.ShouldBe(Manager);
            stored.AssignedAt.ShouldBe(Timestamp.From(Monday));
            answer.Assigned.ShouldContain(learner => learner.PersonId == stored.Learner.Value && learner.AssignmentId == stored.Id.Value);
        }

        var announced = assignments.Published.Select(published => published.ShouldBeOfType<CourseAssignedV1>()).ToList();
        announced.Count.ShouldBe(2);
        foreach (var assigned in answer.Assigned)
        {
            var announcement = announced.Single(each => each.AssignmentId == assigned.AssignmentId);
            announcement.PersonId.ShouldBe(assigned.PersonId);
            announcement.PharmacyId.ShouldBe(Pharmacy.Value);
            announcement.CourseId.ShouldBe(Hygiene.Value);
            announcement.DueDate.ShouldBe(EndOfMarch.Value);
        }

        assignments.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task A_recurrence_is_kept_on_every_assignment()
    {
        await SeedAsync();

        var result = await AssignAsync([Anna, Ben], recurrence: RecurrenceInterval.Yearly);

        result.Value.Assigned.Count.ShouldBe(2);
        assignments.Stored.Select(stored => stored.Recurrence).ShouldBe([RecurrenceInterval.Yearly, RecurrenceInterval.Yearly]);
    }

    [Fact]
    public async Task A_member_who_already_has_an_active_assignment_for_the_course_is_refused_by_name_and_the_others_are_assigned()
    {
        await SeedAsync();
        await AssignAsync([Anna]);

        var result = await AssignAsync([Anna, Ben]);

        var answer = result.Value;
        answer.Assigned.ShouldHaveSingleItem().PersonId.ShouldBe(Ben.Value);
        var refused = answer.Refused.ShouldHaveSingleItem();
        refused.PersonId.ShouldBe(Anna.Value);
        refused.Code.ShouldBe("compliance.assignment-already-active");
        refused.Message.ShouldNotBeNullOrWhiteSpace();
        assignments.Stored.Count(stored => stored.Learner == Anna).ShouldBe(1);
        assignments.Published.Count.ShouldBe(2);
        assignments.Commits.ShouldBe(2);
    }

    [Fact]
    public async Task An_assignment_of_another_course_or_in_another_pharmacy_does_not_stop_a_new_one()
    {
        await SeedAsync();
        var otherCourse = CourseId.From(Guid.CreateVersion7());
        await SeedCourseAsync(otherCourse, 1);
        await AssignAsync([Anna], course: otherCourse);

        var result = await AssignAsync([Anna]);

        result.Value.Assigned.ShouldHaveSingleItem().PersonId.ShouldBe(Anna.Value);
        result.Value.Refused.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_completed_or_cancelled_assignment_does_not_stop_a_new_one()
    {
        await SeedAsync();
        await AssignAsync([Anna, Ben]);
        var anna = assignments.Stored.Single(stored => stored.Learner == Anna);
        var ben = assignments.Stored.Single(stored => stored.Learner == Ben);
        anna.Complete(CertificateId.From(Guid.CreateVersion7()), Timestamp.From(Monday.AddDays(1))).IsSuccess.ShouldBeTrue();
        ben.Cancel(Manager, ChangeReason.From("Versehen"), Timestamp.From(Monday.AddDays(1))).IsSuccess.ShouldBeTrue();

        var result = await AssignAsync([Anna, Ben]);

        result.Value.Assigned.Select(learner => learner.PersonId).ShouldBe([Anna.Value, Ben.Value]);
        result.Value.Refused.ShouldBeEmpty();
        assignments.Stored.Count(stored => stored.Status == AssignmentStatus.Open).ShouldBe(2);
    }

    [Fact]
    public async Task A_person_who_is_no_active_member_of_the_pharmacy_is_refused_by_name()
    {
        await SeedAsync();
        var stranger = PersonId.From(Guid.CreateVersion7());
        var leaver = PersonId.From(Guid.CreateVersion7());
        var ended = await ComplianceMembers.StartAsync(members, leaver, Pharmacy, MembershipRole.Employee);
        await ComplianceMembers.EndAsync(members, ended, leaver, Pharmacy);
        var elsewhere = PersonId.From(Guid.CreateVersion7());
        await ComplianceMembers.StartAsync(members, elsewhere, PharmacyId.From(Guid.CreateVersion7()), MembershipRole.Employee);

        var result = await AssignAsync([stranger, Anna, leaver, elsewhere]);

        var answer = result.Value;
        answer.Assigned.ShouldHaveSingleItem().PersonId.ShouldBe(Anna.Value);
        answer.Refused.Select(refused => refused.PersonId).ShouldBe([stranger.Value, leaver.Value, elsewhere.Value]);
        answer.Refused.ShouldAllBe(refused => refused.Code == "compliance.learner-not-a-member");
        assignments.Stored.ShouldHaveSingleItem().Learner.ShouldBe(Anna);
    }

    [Fact]
    public async Task A_person_named_twice_is_assigned_once()
    {
        await SeedAsync();

        var result = await AssignAsync([Anna, Ben, Anna]);

        result.Value.Assigned.Select(learner => learner.PersonId).ShouldBe([Anna.Value, Ben.Value]);
        result.Value.Refused.ShouldBeEmpty();
        assignments.Stored.Count.ShouldBe(2);
        assignments.Published.Count.ShouldBe(2);
    }

    [Fact]
    public async Task When_every_learner_is_refused_nothing_is_committed_and_the_answer_says_why()
    {
        await SeedAsync();
        await AssignAsync([Anna]);
        var stranger = PersonId.From(Guid.CreateVersion7());

        var result = await AssignAsync([Anna, stranger]);

        var answer = result.Value;
        answer.Assigned.ShouldBeEmpty();
        answer.Refused.Select(refused => (refused.PersonId, refused.Code)).ShouldBe(
        [
            (Anna.Value, "compliance.assignment-already-active"),
            (stranger.Value, "compliance.learner-not-a-member"),
        ]);
        assignments.Commits.ShouldBe(1);
        assignments.Published.Count.ShouldBe(1);
    }

    /// <summary>The due date is the request's, not a learner's: it is checked once, whoever is in the list.</summary>
    [Fact]
    public async Task A_due_date_in_the_past_is_refused_whoever_is_in_the_list()
    {
        await SeedAsync();
        var stranger = PersonId.From(Guid.CreateVersion7());

        var result = await AssignAsync([stranger], dueDate: DueDate.From(new DateOnly(2027, 2, 28)));

        result.IsFailure.ShouldBeTrue("a past due date was accepted because no learner reached the aggregate");
        result.Error.Code.ShouldBe("compliance.due-date-in-the-past");
        result.Error.Status.ShouldBe(HttpStatusCode.UnprocessableEntity);
        assignments.Commits.ShouldBe(0);
    }

    /// <summary>A due date more than ten years ahead is the caller's mistake, and its recurrences could never be computed.</summary>
    [Fact]
    public async Task A_due_date_more_than_ten_years_ahead_is_refused()
    {
        await SeedAsync();

        var result = await AssignAsync([Anna], dueDate: DueDate.From(new DateOnly(2037, 3, 2)));

        result.IsFailure.ShouldBeTrue("a due date more than ten years ahead was accepted");
        result.Error.Code.ShouldBe("compliance.due-date-too-far");
        result.Error.Status.ShouldBe(HttpStatusCode.BadRequest);
        assignments.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task A_due_date_exactly_ten_years_ahead_is_accepted()
    {
        await SeedAsync();

        var result = await AssignAsync([Anna], dueDate: DueDate.From(new DateOnly(2037, 3, 1)));

        result.Value.Assigned.ShouldHaveSingleItem().PersonId.ShouldBe(Anna.Value);
    }

    [Fact]
    public async Task An_employee_may_not_assign()
    {
        await SeedAsync();

        var result = await AssignAsync([Ben], by: Anna);

        result.Error.Code.ShouldBe("compliance.capability-missing");
        result.Error.Status.ShouldBe(HttpStatusCode.Forbidden);
        assignments.Stored.ShouldBeEmpty();
        assignments.Published.ShouldBeEmpty();
        assignments.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task A_demoted_manager_may_no_longer_assign()
    {
        var manager = await SeedAsync();
        await ComplianceMembers.ChangeAsync(members, manager, Manager, Pharmacy, 2, MembershipRole.Employee);

        var result = await AssignAsync([Anna]);

        result.Error.Code.ShouldBe("compliance.capability-missing");
        result.Error.Status.ShouldBe(HttpStatusCode.Forbidden);
        assignments.Stored.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_promoted_employee_may_assign()
    {
        await SeedAsync();
        var anna = MembershipOf(Anna);
        await ComplianceMembers.ChangeAsync(members, anna, Anna, Pharmacy, 2, MembershipRole.Manager);

        var result = await AssignAsync([Ben], by: Anna);

        result.Value.Assigned.ShouldHaveSingleItem().PersonId.ShouldBe(Ben.Value);
        assignments.Stored.ShouldHaveSingleItem().AssignedBy.ShouldBe(Anna);
    }

    [Fact]
    public async Task A_stranger_is_told_the_pharmacy_does_not_exist()
    {
        await SeedAsync();

        var result = await AssignAsync([Anna], by: PersonId.From(Guid.CreateVersion7()));

        result.Error.Code.ShouldBe("compliance.pharmacy-not-found");
        result.Error.Status.ShouldBe(HttpStatusCode.NotFound);
        assignments.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task A_manager_of_another_pharmacy_is_told_the_pharmacy_does_not_exist()
    {
        await SeedAsync();
        var otherManager = PersonId.From(Guid.CreateVersion7());
        await ComplianceMembers.StartAsync(members, otherManager, PharmacyId.From(Guid.CreateVersion7()), MembershipRole.Manager);

        var result = await AssignAsync([Anna], by: otherManager);

        result.Error.Code.ShouldBe("compliance.pharmacy-not-found");
        result.Error.Status.ShouldBe(HttpStatusCode.NotFound);
        assignments.Stored.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_course_compliance_has_not_received_is_not_found()
    {
        await SeedAsync();

        var result = await AssignAsync([Anna], course: CourseId.From(Guid.CreateVersion7()));

        result.Error.Code.ShouldBe("compliance.course-not-found");
        result.Error.Status.ShouldBe(HttpStatusCode.NotFound);
        assignments.Stored.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_course_whose_highest_version_is_retired_is_refused()
    {
        await SeedAsync();
        var second = await SeedCourseAsync(Hygiene, 2);
        await RetireAsync(second, Hygiene);

        var result = await AssignAsync([Anna]);

        result.Error.Code.ShouldBe("compliance.course-retired");
        result.Error.Status.ShouldBe(HttpStatusCode.Conflict);
        assignments.Stored.ShouldBeEmpty();
        assignments.Published.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_course_whose_older_version_is_retired_is_assigned()
    {
        var first = await SeedCourseAsync(Hygiene, 1);
        await SeedMembersAsync();
        await SeedCourseAsync(Hygiene, 2);
        await RetireAsync(first, Hygiene);

        var result = await AssignAsync([Anna]);

        result.Value.Assigned.ShouldHaveSingleItem().PersonId.ShouldBe(Anna.Value);
    }

    [Fact]
    public async Task A_due_date_in_the_past_refuses_the_whole_request()
    {
        await SeedAsync();

        var result = await AssignAsync([Anna, Ben], dueDate: DueDate.From(new DateOnly(2027, 2, 28)));

        result.Error.Code.ShouldBe("compliance.due-date-in-the-past");
        result.Error.Status.ShouldBe(HttpStatusCode.UnprocessableEntity);
        assignments.Stored.ShouldBeEmpty();
        assignments.Published.ShouldBeEmpty();
        assignments.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task A_due_date_of_today_in_germany_is_accepted()
    {
        await SeedAsync();
        time.SetUtcNow(new DateTimeOffset(2027, 3, 1, 23, 30, 0, TimeSpan.Zero));

        var result = await AssignAsync([Anna], dueDate: DueDate.From(new DateOnly(2027, 3, 2)));

        result.Value.Assigned.ShouldHaveSingleItem().PersonId.ShouldBe(Anna.Value);
        assignments.Stored.ShouldHaveSingleItem().AssignedAt.ShouldBe(Timestamp.From(new DateTimeOffset(2027, 3, 1, 23, 30, 0, TimeSpan.Zero)));
    }

    [Fact]
    public async Task No_learners_are_refused()
    {
        await SeedAsync();

        var result = await AssignAsync([]);

        result.Error.Code.ShouldBe("compliance.no-learners");
        result.Error.Status.ShouldBe(HttpStatusCode.BadRequest);
        assignments.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task More_than_a_hundred_learners_are_refused()
    {
        await SeedAsync();
        List<PersonId> learners = [.. Enumerable.Range(0, AssignCourse.MaximumLearners + 1).Select(_ => PersonId.From(Guid.CreateVersion7()))];

        var result = await AssignAsync(learners);

        result.Error.Code.ShouldBe("compliance.too-many-learners");
        result.Error.Status.ShouldBe(HttpStatusCode.BadRequest);
        assignments.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task A_hundred_distinct_learners_are_not_too_many_even_when_one_is_named_twice()
    {
        await SeedAsync();
        List<PersonId> learners = [.. Enumerable.Range(0, AssignCourse.MaximumLearners - 1).Select(_ => PersonId.From(Guid.CreateVersion7())), Anna, Anna];

        var result = await AssignAsync(learners);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Assigned.ShouldHaveSingleItem().PersonId.ShouldBe(Anna.Value);
        result.Value.Refused.Count.ShouldBe(AssignCourse.MaximumLearners - 1);
    }

    [Fact]
    public void At_most_a_hundred_learners_are_assigned_at_once()
    {
        AssignCourse.MaximumLearners.ShouldBe(100);
    }

    [Fact]
    public async Task A_save_that_lost_a_race_is_refused_and_announces_nothing()
    {
        await SeedAsync();
        assignments.LoseNextCommit();

        var result = await AssignAsync([Anna, Ben]);

        result.Error.Code.ShouldBe("compliance.assignments-changed");
        result.Error.Status.ShouldBe(HttpStatusCode.Conflict);
        assignments.Stored.ShouldBeEmpty();
        assignments.Published.ShouldBeEmpty();
    }

    /// <summary>The manager, Anna and Ben as members of the pharmacy, and the course at version 1; answers the manager's membership.</summary>
    private async Task<MembershipId> SeedAsync()
    {
        var manager = await SeedMembersAsync();
        await SeedCourseAsync(Hygiene, 1);
        return manager;
    }

    private async Task<MembershipId> SeedMembersAsync()
    {
        var manager = await ComplianceMembers.StartAsync(members, Manager, Pharmacy, MembershipRole.Manager);
        await ComplianceMembers.StartAsync(members, Anna, Pharmacy, MembershipRole.Employee);
        await ComplianceMembers.StartAsync(members, Ben, Pharmacy, MembershipRole.Employee);
        return manager;
    }

    private async Task<CourseVersionId> SeedCourseAsync(CourseId course, int number)
    {
        var version = CourseVersionId.From(Guid.CreateVersion7());
        courses.Add(CourseVersion.Published(version, course, VersionNumber.From(number), CourseTitle.From("Hygiene in der Apotheke"), ValidityMonths.From(24)));
        await courses.CommitAsync(TestContext.Current.CancellationToken);
        return version;
    }

    private async Task RetireAsync(CourseVersionId version, CourseId course)
    {
        courses.Add(CourseVersionRetirement.Of(version, course));
        await courses.CommitAsync(TestContext.Current.CancellationToken);
    }

    private MembershipId MembershipOf(PersonId person) =>
        members.Members.Single(member => member.Person == person && member.Pharmacy == Pharmacy).Id;

    private Task<Result<ComplianceCourseAssignmentDto, ApiError>> AssignAsync(
        IReadOnlyList<PersonId> learners,
        PersonId? by = null,
        CourseId? course = null,
        DueDate? dueDate = null,
        RecurrenceInterval? recurrence = null) =>
        new AssignCourseHandler(members, courses, assignments, assignments, time).HandleAsync(
            new AssignCourse(Pharmacy, course ?? Hygiene, learners, dueDate ?? EndOfMarch, recurrence, by ?? Manager),
            TestContext.Current.CancellationToken);
}
