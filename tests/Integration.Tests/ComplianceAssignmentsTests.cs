using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// A manager assigns a course to several members of their pharmacy through the edge,
/// <c>POST /compliance/pharmacies/{id}/assignments</c> (#29, D8): whether the caller may is decided from Compliance's own
/// copy of their membership and its current role, each learner is assigned or refused by name, and at most one active
/// assignment per learner, pharmacy and course is stored however the requests race. Members and courses reach Compliance
/// as their events, delivered and awaited one after the other; each test has a pharmacy and a course of its own.
/// </summary>
public class ComplianceAssignmentsTests(AspireFixture aspire)
{
    private const string StartedType = "SmartSolutionsLab.PharmaTraining.Contracts.Organisation.MembershipStartedV1";
    private const string PublishedType = "SmartSolutionsLab.PharmaTraining.Contracts.Editorial.CourseVersionPublishedV1";
    private const string RetiredType = "SmartSolutionsLab.PharmaTraining.Contracts.Editorial.CourseVersionRetiredV1";
    private const string SessionStartedType = "SmartSolutionsLab.PharmaTraining.Contracts.Learning.LearningSessionStartedV1";
    private const string ContentCompletedType = "SmartSolutionsLab.PharmaTraining.Contracts.Learning.ContentCompletedV1";
    private const string AttemptsExhaustedType = "SmartSolutionsLab.PharmaTraining.Contracts.Assessment.AttemptsExhaustedV1";
    private const string OfLearner = "select count(*) from compliance.assignments where learner_id = @id";
    private const string OpenInPharmacy = "select count(*) from compliance.assignments where pharmacy_id = @id and status = 'Open'";
    private const string ActiveInPharmacy = "select count(*) from compliance.assignments where pharmacy_id = @id and status in ('Open', 'InProgress')";
    private const string WithEmptyHistoryInPharmacy = "select count(*) from compliance.assignments where pharmacy_id = @id and history = '[]'::jsonb";
    private const string ChangedToVersionTwo = "select count(*) from compliance.membership_changes where membership_id = @id and version = 2";

    private static readonly Uri Pharmacies = new("/organisation/pharmacies", UriKind.Relative);
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    private readonly OrganisationDatabase organisation = new(aspire);

    [Fact]
    public async Task A_manager_assigns_a_course_to_two_employees_and_both_are_stored_open()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var setting = await PharmacyWithCourseAsync(cancellationToken);
        using var manager = await aspire.CreateHostClientAsync("developer");

        using var response = await AssignAsync(manager, setting.Pharmacy, Request(setting.Course, [setting.EmployeeA, setting.EmployeeB]), cancellationToken);

        var answer = await AnswerOfAsync(response, cancellationToken);
        answer.Assigned.Select(learner => learner.PersonId).ShouldBe([setting.EmployeeA, setting.EmployeeB], ignoreOrder: true);
        answer.Assigned.Select(learner => learner.AssignmentId).Distinct().Count().ShouldBe(2);
        answer.Refused.ShouldBeEmpty();
        (await CountAsync(OpenInPharmacy, setting.Pharmacy, cancellationToken)).ShouldBe(2);
        (await CountAsync(WithEmptyHistoryInPharmacy, setting.Pharmacy, cancellationToken)).ShouldBe(2);
        var stored = await AssignmentsAsync(setting.Pharmacy, cancellationToken);
        stored.Select(row => row.LearnerId).ShouldBe([setting.EmployeeA, setting.EmployeeB], ignoreOrder: true);
        stored.ShouldAllBe(row => row.CourseId == setting.Course && row.AssignedBy == setting.Manager && row.DueDate == InAMonth() && row.RecurrenceMonths == null);
    }

    [Fact]
    public async Task A_recurrence_is_stored_with_each_assignment()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var setting = await PharmacyWithCourseAsync(cancellationToken);
        using var manager = await aspire.CreateHostClientAsync("developer");

        using var response = await AssignAsync(manager, setting.Pharmacy, Request(setting.Course, [setting.EmployeeA], recurrenceMonths: 12), cancellationToken);

        (await AnswerOfAsync(response, cancellationToken)).Assigned.ShouldHaveSingleItem();
        (await AssignmentsAsync(setting.Pharmacy, cancellationToken)).ShouldHaveSingleItem().RecurrenceMonths.ShouldBe(12);
    }

    [Fact]
    public async Task An_employee_is_forbidden()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var setting = await PharmacyWithCourseAsync(cancellationToken);
        using var employee = await aspire.CreateHostClientAsync("employee-a");

        using var response = await AssignAsync(employee, setting.Pharmacy, Request(setting.Course, [setting.EmployeeB]), cancellationToken);

        await ShouldBeRefusedAsync(response, HttpStatusCode.Forbidden, "compliance.capability-missing", cancellationToken);
        (await CountAsync(ActiveInPharmacy, setting.Pharmacy, cancellationToken)).ShouldBe(0);
    }

    [Fact]
    public async Task A_signed_in_non_member_is_told_the_pharmacy_does_not_exist()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var setting = await PharmacyWithCourseAsync(cancellationToken);
        using var visitor = await aspire.CreateHostClientAsync("visitor");

        using var response = await AssignAsync(visitor, setting.Pharmacy, Request(setting.Course, [setting.EmployeeA]), cancellationToken);

        await ShouldBeRefusedAsync(response, HttpStatusCode.NotFound, "compliance.pharmacy-not-found", cancellationToken);
        (await CountAsync(ActiveInPharmacy, setting.Pharmacy, cancellationToken)).ShouldBe(0);
    }

    [Fact]
    public async Task An_anonymous_caller_is_unauthorized()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var anonymous = aspire.CreateHostClient();

        using var response = await AssignAsync(anonymous, Guid.CreateVersion7(), Request(Guid.CreateVersion7(), [Guid.CreateVersion7()]), cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, await response.Content.ReadAsStringAsync(cancellationToken));
    }

    [Fact]
    public async Task A_due_date_in_the_past_is_refused_and_nothing_stored()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var setting = await PharmacyWithCourseAsync(cancellationToken);
        using var manager = await aspire.CreateHostClientAsync("developer");

        using var response = await AssignAsync(manager, setting.Pharmacy, Request(setting.Course, [setting.EmployeeA, setting.EmployeeB], dueDate: Today().AddDays(-2)), cancellationToken);

        await ShouldBeRefusedAsync(response, HttpStatusCode.UnprocessableEntity, "compliance.due-date-in-the-past", cancellationToken);
        (await CountAsync(ActiveInPharmacy, setting.Pharmacy, cancellationToken)).ShouldBe(0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(61)]
    public async Task A_recurrence_outside_one_to_sixty_months_is_the_callers_mistake(int months)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var manager = await aspire.CreateHostClientAsync("developer");

        using var response = await AssignAsync(manager, Guid.CreateVersion7(), Request(Guid.CreateVersion7(), [Guid.CreateVersion7()], recurrenceMonths: months), cancellationToken);

        await ShouldBeRefusedAsync(response, HttpStatusCode.BadRequest, "compliance.invalid-recurrence", cancellationToken);
    }

    [Fact]
    public async Task An_empty_pharmacy_id_is_no_pharmacy()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var manager = await aspire.CreateHostClientAsync("developer");

        using var response = await AssignAsync(manager, Guid.Empty, Request(Guid.CreateVersion7(), [Guid.CreateVersion7()]), cancellationToken);

        await ShouldBeRefusedAsync(response, HttpStatusCode.NotFound, "compliance.pharmacy-not-found", cancellationToken);
    }

    [Fact]
    public async Task An_empty_course_id_is_no_course()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var manager = await aspire.CreateHostClientAsync("developer");

        using var response = await AssignAsync(manager, Guid.CreateVersion7(), Request(Guid.Empty, [Guid.CreateVersion7()]), cancellationToken);

        await ShouldBeRefusedAsync(response, HttpStatusCode.NotFound, "compliance.course-not-found", cancellationToken);
    }

    [Fact]
    public async Task Missing_learners_are_the_callers_mistake()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var manager = await aspire.CreateHostClientAsync("developer");

        using var response = await AssignAsync(manager, Guid.CreateVersion7(), new { courseId = Guid.CreateVersion7(), personIds = (Guid[]?)null, dueDate = InAMonth(), recurrenceMonths = (int?)null }, cancellationToken);

        await ShouldBeRefusedAsync(response, HttpStatusCode.BadRequest, "compliance.no-learners", cancellationToken);
    }

    [Fact]
    public async Task No_learners_are_the_callers_mistake()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var setting = await PharmacyWithCourseAsync(cancellationToken);
        using var manager = await aspire.CreateHostClientAsync("developer");

        using var response = await AssignAsync(manager, setting.Pharmacy, Request(setting.Course, []), cancellationToken);

        await ShouldBeRefusedAsync(response, HttpStatusCode.BadRequest, "compliance.no-learners", cancellationToken);
    }

    [Fact]
    public async Task An_empty_person_id_is_the_callers_mistake()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var manager = await aspire.CreateHostClientAsync("developer");

        using var response = await AssignAsync(manager, Guid.CreateVersion7(), Request(Guid.CreateVersion7(), [Guid.CreateVersion7(), Guid.Empty]), cancellationToken);

        await ShouldBeRefusedAsync(response, HttpStatusCode.BadRequest, "compliance.invalid-person", cancellationToken);
    }

    /// <summary>A body without a due date is refused for it, not read as 0001-01-01 and then told the date is past.</summary>
    [Fact]
    public async Task A_missing_due_date_is_the_callers_mistake()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var setting = await PharmacyWithCourseAsync(cancellationToken);
        using var manager = await aspire.CreateHostClientAsync("developer");

        using var response = await AssignAsync(manager, setting.Pharmacy, new { courseId = setting.Course, personIds = new[] { setting.EmployeeA, Guid.CreateVersion7() } }, cancellationToken);

        await ShouldBeRefusedAsync(response, HttpStatusCode.BadRequest, "compliance.due-date-missing", cancellationToken);
        (await CountAsync(ActiveInPharmacy, setting.Pharmacy, cancellationToken)).ShouldBe(0);
    }

    /// <summary>A body far beyond the cap is refused before its ids are mapped one by one; repeats count, as they are read.</summary>
    [Fact]
    public async Task A_body_with_far_more_person_ids_than_the_cap_is_refused_before_anything_else()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var manager = await aspire.CreateHostClientAsync("developer");
        var repeated = Guid.CreateVersion7();

        using var response = await AssignAsync(manager, Guid.CreateVersion7(), Request(Guid.CreateVersion7(), [.. Enumerable.Repeat(repeated, 1001)]), cancellationToken);

        await ShouldBeRefusedAsync(response, HttpStatusCode.BadRequest, "compliance.too-many-learners", cancellationToken);
    }

    /// <summary>A body without a course is refused for it, not read as the empty id and told that course does not exist.</summary>
    [Fact]
    public async Task A_missing_course_id_is_the_callers_mistake()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var manager = await aspire.CreateHostClientAsync("developer");

        using var response = await AssignAsync(manager, Guid.CreateVersion7(), new { personIds = new[] { Guid.CreateVersion7() }, dueDate = InAMonth() }, cancellationToken);

        await ShouldBeRefusedAsync(response, HttpStatusCode.BadRequest, "compliance.course-missing", cancellationToken);
    }

    /// <summary>The route reads at most what a thousand ids need; a larger body is refused by the server before it is bound.</summary>
    [Fact]
    public async Task A_body_larger_than_a_thousand_ids_need_is_refused_before_it_is_read()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var manager = await aspire.CreateHostClientAsync("developer");
        var persons = Enumerable.Range(0, 3000).Select(_ => Guid.CreateVersion7()).ToArray();

        using var response = await AssignAsync(manager, Guid.CreateVersion7(), Request(Guid.CreateVersion7(), persons), cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge, await response.Content.ReadAsStringAsync(cancellationToken));
    }

    [Fact]
    public async Task Assigning_again_reports_the_person_who_already_has_an_active_assignment()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var setting = await PharmacyWithCourseAsync(cancellationToken);
        using var manager = await aspire.CreateHostClientAsync("developer");
        using var first = await AssignAsync(manager, setting.Pharmacy, Request(setting.Course, [setting.EmployeeA]), cancellationToken);
        (await AnswerOfAsync(first, cancellationToken)).Assigned.ShouldHaveSingleItem();

        using var again = await AssignAsync(manager, setting.Pharmacy, Request(setting.Course, [setting.EmployeeA]), cancellationToken);

        var answer = await AnswerOfAsync(again, cancellationToken);
        answer.Assigned.ShouldBeEmpty();
        var refused = answer.Refused.ShouldHaveSingleItem();
        refused.PersonId.ShouldBe(setting.EmployeeA);
        refused.Code.ShouldBe("compliance.assignment-already-active");
        refused.Message.ShouldNotBeNullOrWhiteSpace();
        (await CountAsync(ActiveInPharmacy, setting.Pharmacy, cancellationToken)).ShouldBe(1);
    }

    [Fact]
    public async Task A_person_who_is_no_member_is_refused_by_name_and_the_others_are_assigned()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var setting = await PharmacyWithCourseAsync(cancellationToken);
        using var manager = await aspire.CreateHostClientAsync("developer");
        var stranger = Guid.CreateVersion7();

        using var response = await AssignAsync(manager, setting.Pharmacy, Request(setting.Course, [stranger, setting.EmployeeA]), cancellationToken);

        var answer = await AnswerOfAsync(response, cancellationToken);
        answer.Assigned.ShouldHaveSingleItem().PersonId.ShouldBe(setting.EmployeeA);
        var refused = answer.Refused.ShouldHaveSingleItem();
        refused.PersonId.ShouldBe(stranger);
        refused.Code.ShouldBe("compliance.learner-not-a-member");
        (await CountAsync(ActiveInPharmacy, setting.Pharmacy, cancellationToken)).ShouldBe(1);
    }

    [Fact]
    public async Task Of_two_assignments_of_one_person_and_course_at_once_exactly_one_is_stored()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var setting = await PharmacyWithCourseAsync(cancellationToken);
        using var manager = await aspire.CreateHostClientAsync("developer");

        var responses = await Task.WhenAll(
            AssignAsync(manager, setting.Pharmacy, Request(setting.Course, [setting.EmployeeA, setting.EmployeeB]), cancellationToken),
            AssignAsync(manager, setting.Pharmacy, Request(setting.Course, [setting.EmployeeB, setting.EmployeeA]), cancellationToken));
        try
        {
            List<string> answers = [];
            var assignedA = 0;
            foreach (var response in responses)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                answers.Add($"{(int)response.StatusCode} {body}");
                response.StatusCode.ShouldBeOneOf([HttpStatusCode.OK, HttpStatusCode.Conflict], string.Join(" | ", answers));
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    var answer = JsonSerializer.Deserialize<AssignmentAnswer>(body, JsonSerializerOptions.Web).ShouldNotBeNull();
                    assignedA += answer.Assigned.Count(learner => learner.PersonId == setting.EmployeeA);
                    answer.Refused.ShouldAllBe(refused => refused.Code == "compliance.assignment-already-active");
                }
                else
                {
                    var problem = JsonSerializer.Deserialize<Problem>(body, JsonSerializerOptions.Web);
                    (problem?.Code).ShouldBeOneOf(["already-exists", "compliance.assignments-changed"], body);
                }
            }

            var described = string.Join(" | ", answers);
            assignedA.ShouldBe(1, described);
            (await CountAsync(ActiveInPharmacy, setting.Pharmacy, cancellationToken)).ShouldBe(2, described);
            var stored = await AssignmentsAsync(setting.Pharmacy, cancellationToken);
            stored.Count(row => row.LearnerId == setting.EmployeeA).ShouldBe(1, described);
            stored.Count(row => row.LearnerId == setting.EmployeeB).ShouldBe(1, described);
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    [Fact]
    public async Task A_course_published_v2_then_v1_whose_v2_is_retired_cannot_be_assigned()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var setting = await PharmacyAsync(cancellationToken);
        var course = Guid.CreateVersion7();
        var second = Published(course, 2);
        var first = Published(course, 1);
        await DeliverAsync(PublishedType, second, cancellationToken);
        await DeliverAsync(PublishedType, first, cancellationToken);
        await DeliverAsync(RetiredType, Retired(second), cancellationToken);
        using var manager = await aspire.CreateHostClientAsync("developer");

        using var response = await AssignAsync(manager, setting.Pharmacy, Request(course, [setting.EmployeeA]), cancellationToken);

        await ShouldBeRefusedAsync(response, HttpStatusCode.Conflict, "compliance.course-retired", cancellationToken);
        (await CountAsync(ActiveInPharmacy, setting.Pharmacy, cancellationToken)).ShouldBe(0);
    }

    [Fact]
    public async Task A_retirement_arriving_before_its_version_is_held_and_applied_when_the_version_arrives()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var setting = await PharmacyAsync(cancellationToken);
        var course = Guid.CreateVersion7();
        var first = Published(course, 1);
        var second = Published(course, 2);
        using var manager = await aspire.CreateHostClientAsync("developer");

        await DeliverAsync(RetiredType, Retired(second), cancellationToken);
        using (var beforeAnyVersion = await AssignAsync(manager, setting.Pharmacy, Request(course, [setting.EmployeeA]), cancellationToken))
        {
            await ShouldBeRefusedAsync(beforeAnyVersion, HttpStatusCode.NotFound, "compliance.course-not-found", cancellationToken);
        }

        await DeliverAsync(PublishedType, first, cancellationToken);
        using (var onTheFirstVersion = await AssignAsync(manager, setting.Pharmacy, Request(course, [setting.EmployeeA]), cancellationToken))
        {
            (await AnswerOfAsync(onTheFirstVersion, cancellationToken)).Assigned.ShouldHaveSingleItem().PersonId.ShouldBe(setting.EmployeeA);
        }

        await DeliverAsync(PublishedType, second, cancellationToken);
        using var onceTheRetiredVersionArrived = await AssignAsync(manager, setting.Pharmacy, Request(course, [setting.EmployeeB]), cancellationToken);
        await ShouldBeRefusedAsync(onceTheRetiredVersionArrived, HttpStatusCode.Conflict, "compliance.course-retired", cancellationToken);
    }

    [Fact]
    public async Task A_retirement_of_an_older_version_arriving_first_leaves_the_course_assignable()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var setting = await PharmacyAsync(cancellationToken);
        var course = Guid.CreateVersion7();
        var first = Published(course, 1);
        var second = Published(course, 2);
        await DeliverAsync(RetiredType, Retired(first), cancellationToken);
        await DeliverAsync(PublishedType, second, cancellationToken);
        await DeliverAsync(PublishedType, first, cancellationToken);
        using var manager = await aspire.CreateHostClientAsync("developer");

        using var response = await AssignAsync(manager, setting.Pharmacy, Request(course, [setting.EmployeeA]), cancellationToken);

        (await AnswerOfAsync(response, cancellationToken)).Assigned.ShouldHaveSingleItem().PersonId.ShouldBe(setting.EmployeeA);
    }

    [Fact]
    public async Task A_manager_demoted_in_organisation_can_no_longer_assign()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = await aspire.CreateHostClientAsync("owner");
        using var developer = await aspire.CreateHostClientAsync("developer");
        var pharmacy = await RegisterAsync(owner, cancellationToken);
        var ownerPerson = await organisation.SubjectOfAsync("owner");
        var developerPerson = await organisation.SubjectOfAsync("developer");
        var employee = await organisation.SubjectOfAsync("employee-a");
        var developerMembership = await organisation.SeedMemberAsync(pharmacy, "developer", "Manager", "Pharmacist", cancellationToken);
        await DeliverAsync(StartedType, Started(developerMembership, developerPerson, pharmacy, await organisation.NameOfAsync("developer"), "Pharmacist", "Manager"), cancellationToken);
        await DeliverAsync(StartedType, Started(Guid.CreateVersion7(), employee, pharmacy, await organisation.NameOfAsync("employee-a"), "Pta", "Employee"), cancellationToken);
        var course = Published(Guid.CreateVersion7(), 1);
        await DeliverAsync(PublishedType, course, cancellationToken);
        await WaitUntilAsync(
            async () => await CountAsync("select count(*) from compliance.members where pharmacy_id = @id", pharmacy, cancellationToken) == 3,
            $"Compliance holds the owner's membership of pharmacy {pharmacy}, which Organisation announced when it was registered",
            cancellationToken);
        using (var before = await AssignAsync(developer, pharmacy, Request(course.CourseId, [employee]), cancellationToken))
        {
            (await AnswerOfAsync(before, cancellationToken)).Assigned.ShouldHaveSingleItem();
        }

        using var demoted = await owner.PatchAsJsonAsync(MemberUri(pharmacy, developerMembership), new { jobFunction = "Pharmacist", role = "Employee" }, cancellationToken);
        demoted.StatusCode.ShouldBe(HttpStatusCode.OK, await demoted.Content.ReadAsStringAsync(cancellationToken));
        await WaitUntilAsync(
            async () => await CountAsync(ChangedToVersionTwo, developerMembership, cancellationToken) == 1,
            $"Compliance stores version 2 of membership {developerMembership}",
            cancellationToken);
        var otherCourse = Published(Guid.CreateVersion7(), 1);
        await DeliverAsync(PublishedType, otherCourse, cancellationToken);

        using var refused = await AssignAsync(developer, pharmacy, Request(otherCourse.CourseId, [employee]), cancellationToken);
        await ShouldBeRefusedAsync(refused, HttpStatusCode.Forbidden, "compliance.capability-missing", cancellationToken);

        using var byTheOwner = await AssignAsync(owner, pharmacy, Request(otherCourse.CourseId, [employee]), cancellationToken);
        var answer = await AnswerOfAsync(byTheOwner, cancellationToken);
        answer.Assigned.ShouldHaveSingleItem().PersonId.ShouldBe(employee);
        (await AssignmentsAsync(pharmacy, cancellationToken)).Single(row => row.CourseId == otherCourse.CourseId).AssignedBy.ShouldBe(ownerPerson);
    }

    [Fact]
    public async Task A_started_session_puts_the_learners_assignment_in_progress()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (setting, assignment, origin) = await AssignedAsync(cancellationToken);
        var learning = new Learning(setting.EmployeeA, setting.Course);

        await DeliverAsync(SessionStartedType, learning.Event(origin), cancellationToken);

        var progress = await ProgressAsync(assignment, cancellationToken);
        progress.Status.ShouldBe("InProgress");
        progress.StartedAt.ShouldBe(origin);
        progress.TestAccess.ShouldBe("Available");
        progress.TestAccessChangedAt.ShouldBeNull();
    }

    [Fact]
    public async Task Exhausted_attempts_lock_the_assignment_and_relearned_content_unlocks_it()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (setting, assignment, origin) = await AssignedAsync(cancellationToken);
        var learning = new Learning(setting.EmployeeA, setting.Course);
        await DeliverAsync(SessionStartedType, learning.Event(origin), cancellationToken);

        await DeliverAsync(AttemptsExhaustedType, learning.Exhausted(origin.AddSeconds(2)), cancellationToken);

        var locked = await ProgressAsync(assignment, cancellationToken);
        locked.TestAccess.ShouldBe("LockedAfterFailedAttempts");
        locked.TestAccessChangedAt.ShouldBe(origin.AddSeconds(2));

        await DeliverAsync(ContentCompletedType, learning.Event(origin.AddSeconds(3)), cancellationToken);

        var unlocked = await ProgressAsync(assignment, cancellationToken);
        unlocked.TestAccess.ShouldBe("Available");
        unlocked.TestAccessChangedAt.ShouldBe(origin.AddSeconds(3));
        unlocked.Status.ShouldBe("InProgress");
    }

    /// <summary>The owner's case on #32: the completion that opened the examination arrives after the lock it led to.</summary>
    [Fact]
    public async Task Content_completed_of_the_same_cycle_arriving_after_the_lock_leaves_it_locked()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (setting, assignment, origin) = await AssignedAsync(cancellationToken);
        var learning = new Learning(setting.EmployeeA, setting.Course);
        await DeliverAsync(SessionStartedType, learning.Event(origin), cancellationToken);
        var completed = learning.Event(origin.AddSeconds(1));

        await DeliverAsync(AttemptsExhaustedType, learning.Exhausted(origin.AddSeconds(2)), cancellationToken);
        await DeliverAsync(ContentCompletedType, completed, cancellationToken);
        await DeliverAsync(ContentCompletedType, completed, cancellationToken);

        var progress = await ProgressAsync(assignment, cancellationToken);
        progress.TestAccess.ShouldBe("LockedAfterFailedAttempts");
        progress.TestAccessChangedAt.ShouldBe(origin.AddSeconds(2));
        progress.Status.ShouldBe("InProgress");
    }

    [Fact]
    public async Task A_redelivered_lock_after_the_unlock_leaves_it_unlocked()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (setting, assignment, origin) = await AssignedAsync(cancellationToken);
        var learning = new Learning(setting.EmployeeA, setting.Course);
        await DeliverAsync(SessionStartedType, learning.Event(origin), cancellationToken);
        var exhausted = learning.Exhausted(origin.AddSeconds(2));

        await DeliverAsync(AttemptsExhaustedType, exhausted, cancellationToken);
        await DeliverAsync(ContentCompletedType, learning.Event(origin.AddSeconds(3)), cancellationToken);
        await DeliverAsync(AttemptsExhaustedType, exhausted, cancellationToken);

        var progress = await ProgressAsync(assignment, cancellationToken);
        progress.TestAccess.ShouldBe("Available");
        progress.TestAccessChangedAt.ShouldBe(origin.AddSeconds(3));
    }

    [Fact]
    public async Task Progress_of_a_person_without_an_assignment_is_handled_and_changes_nothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var setting = await PharmacyWithCourseAsync(cancellationToken);
        var stranger = Guid.CreateVersion7();
        var learning = new Learning(stranger, setting.Course);
        var origin = WholeSecondAfterNow();

        await DeliverAsync(SessionStartedType, learning.Event(origin), cancellationToken);
        await DeliverAsync(AttemptsExhaustedType, learning.Exhausted(origin.AddSeconds(2)), cancellationToken);
        await DeliverAsync(ContentCompletedType, learning.Event(origin.AddSeconds(3)), cancellationToken);

        (await CountAsync(OfLearner, stranger, cancellationToken)).ShouldBe(0);
    }

    private static DateOnly Today() => DateOnly.FromDateTime(TimeProvider.System.GetUtcNow().UtcDateTime);

    private static DateOnly InAMonth() => Today().AddDays(30);

    private static object Request(Guid course, Guid[] persons, DateOnly? dueDate = null, int? recurrenceMonths = null) =>
        new { courseId = course, personIds = persons, dueDate = dueDate ?? InAMonth(), recurrenceMonths };

    private static Uri AssignmentsUri(Guid pharmacy) => new($"/compliance/pharmacies/{pharmacy}/assignments", UriKind.Relative);

    private static Uri MemberUri(Guid pharmacy, Guid membership) => new($"/organisation/pharmacies/{pharmacy}/members/{membership}", UriKind.Relative);

    private static Task<HttpResponseMessage> AssignAsync(HttpClient client, Guid pharmacy, object request, CancellationToken cancellationToken) =>
        client.PostAsJsonAsync(AssignmentsUri(pharmacy), request, cancellationToken);

    private static async Task<AssignmentAnswer> AnswerOfAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        return JsonSerializer.Deserialize<AssignmentAnswer>(body, JsonSerializerOptions.Web).ShouldNotBeNull();
    }

    private static async Task ShouldBeRefusedAsync(HttpResponseMessage response, HttpStatusCode status, string code, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        response.StatusCode.ShouldBe(status, body);
        var problem = string.IsNullOrWhiteSpace(body) ? null : JsonSerializer.Deserialize<Problem>(body, JsonSerializerOptions.Web);
        (problem?.Code).ShouldBe(code, $"{(int)response.StatusCode} with body: {body}");
    }

    private static async Task<Guid> RegisterAsync(HttpClient client, CancellationToken cancellationToken)
    {
        using var created = await client.PostAsJsonAsync(Pharmacies, new { name = $"Apotheke {Guid.CreateVersion7()}", jobFunction = "Pharmacist" }, cancellationToken);
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        return await created.Content.ReadFromJsonAsync<Guid>(cancellationToken);
    }

    private static StartedEvent Started(Guid membershipId, Guid personId, Guid pharmacyId, string displayName, string jobFunction, string role) =>
        new(membershipId, personId, pharmacyId, displayName, jobFunction, role, TimeProvider.System.GetUtcNow(), NewMetadata());

    private static PublishedEvent Published(Guid courseId, int versionNumber) =>
        new(
            courseId,
            Guid.CreateVersion7(),
            versionNumber,
            $"Hygiene {versionNumber} {Guid.CreateVersion7()}",
            24,
            [new SentChapter(Guid.CreateVersion7(), 1, "Grundlagen", "Text", null, "# Grundlagen", null)],
            NewMetadata());

    private static RetiredEvent Retired(PublishedEvent published) => new(published.CourseId, published.CourseVersionId, NewMetadata());

    private static EventMetadata NewMetadata() => new(Guid.CreateVersion7(), TimeProvider.System.GetUtcNow());

    /// <summary>
    /// The next whole second from now: later than an assignment made before it, and exact in a timestamptz column, which
    /// keeps microseconds only.
    /// </summary>
    private static DateTimeOffset WholeSecondAfterNow()
    {
        var now = TimeProvider.System.GetUtcNow();
        return new DateTimeOffset(now.Ticks - (now.Ticks % TimeSpan.TicksPerSecond), TimeSpan.Zero).AddSeconds(1);
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition, string what, CancellationToken cancellationToken)
    {
        var started = TimeProvider.System.GetTimestamp();
        while (!await condition())
        {
            if (TimeProvider.System.GetElapsedTime(started) >= WaitTimeout)
            {
                throw new TimeoutException($"Waited {WaitTimeout.TotalSeconds} s until {what}.");
            }

            await Task.Delay(PollInterval, cancellationToken);
        }
    }

    /// <summary>A pharmacy of its own in Compliance: developer as its manager, employee-a and employee-b as its employees.</summary>
    private async Task<Setting> PharmacyAsync(CancellationToken cancellationToken)
    {
        var pharmacy = Guid.CreateVersion7();
        var manager = await organisation.SubjectOfAsync("developer");
        var employeeA = await organisation.SubjectOfAsync("employee-a");
        var employeeB = await organisation.SubjectOfAsync("employee-b");
        await DeliverAsync(StartedType, Started(Guid.CreateVersion7(), manager, pharmacy, await organisation.NameOfAsync("developer"), "Pharmacist", "Manager"), cancellationToken);
        await DeliverAsync(StartedType, Started(Guid.CreateVersion7(), employeeA, pharmacy, await organisation.NameOfAsync("employee-a"), "Pta", "Employee"), cancellationToken);
        await DeliverAsync(StartedType, Started(Guid.CreateVersion7(), employeeB, pharmacy, await organisation.NameOfAsync("employee-b"), "Pka", "Employee"), cancellationToken);
        return new Setting(pharmacy, Guid.Empty, manager, employeeA, employeeB);
    }

    /// <summary>The pharmacy of <see cref="PharmacyAsync"/> and a course of its own at version 1.</summary>
    private async Task<Setting> PharmacyWithCourseAsync(CancellationToken cancellationToken)
    {
        var setting = await PharmacyAsync(cancellationToken);
        var course = Published(Guid.CreateVersion7(), 1);
        await DeliverAsync(PublishedType, course, cancellationToken);
        return setting with { Course = course.CourseId };
    }

    private Task DeliverAsync<T>(string type, T message, CancellationToken cancellationToken) =>
        ComplianceDatabase.DeliverAsync(aspire, type, message, cancellationToken);

    private Task<long> CountAsync(string query, Guid id, CancellationToken cancellationToken) =>
        ComplianceDatabase.CountAsync(aspire, query, id, cancellationToken);

    /// <summary>
    /// The pharmacy and course of <see cref="PharmacyWithCourseAsync"/>, the course assigned to employee-a, and the
    /// instant the learner's events start from, taken after the assignment was made.
    /// </summary>
    private async Task<(Setting Setting, Guid Assignment, DateTimeOffset Origin)> AssignedAsync(CancellationToken cancellationToken)
    {
        var setting = await PharmacyWithCourseAsync(cancellationToken);
        using var manager = await aspire.CreateHostClientAsync("developer");
        using var response = await AssignAsync(manager, setting.Pharmacy, Request(setting.Course, [setting.EmployeeA]), cancellationToken);
        var assignment = (await AnswerOfAsync(response, cancellationToken)).Assigned.ShouldHaveSingleItem().AssignmentId;
        return (setting, assignment, WholeSecondAfterNow());
    }

    private async Task<Progress> ProgressAsync(Guid assignment, CancellationToken cancellationToken)
    {
        await using var connection = await ComplianceDatabase.OpenAsync(aspire, cancellationToken);
        await using var command = new NpgsqlCommand(
            "select status, test_access, started_at, test_access_changed_at from compliance.assignments where id = @id",
            connection);
        command.Parameters.AddWithValue("id", assignment);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        (await reader.ReadAsync(cancellationToken)).ShouldBeTrue($"no assignment {assignment}");
        return new Progress(
            reader.GetString(0),
            reader.GetString(1),
            await reader.IsDBNullAsync(2, cancellationToken) ? null : reader.GetFieldValue<DateTimeOffset>(2),
            await reader.IsDBNullAsync(3, cancellationToken) ? null : reader.GetFieldValue<DateTimeOffset>(3));
    }

    private async Task<List<AssignmentRow>> AssignmentsAsync(Guid pharmacy, CancellationToken cancellationToken)
    {
        await using var connection = await ComplianceDatabase.OpenAsync(aspire, cancellationToken);
        await using var command = new NpgsqlCommand(
            "select learner_id, course_id, due_date, recurrence_months, assigned_by, status from compliance.assignments where pharmacy_id = @id",
            connection);
        command.Parameters.AddWithValue("id", pharmacy);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        List<AssignmentRow> rows = [];
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new AssignmentRow(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetFieldValue<DateOnly>(2),
                await reader.IsDBNullAsync(3, cancellationToken) ? null : reader.GetInt32(3),
                reader.GetGuid(4),
                reader.GetString(5)));
        }

        return rows;
    }

    private sealed record Setting(Guid Pharmacy, Guid Course, Guid Manager, Guid EmployeeA, Guid EmployeeB);

    private sealed record AssignmentAnswer(IReadOnlyList<AssignedLearner> Assigned, IReadOnlyList<RefusedLearner> Refused);

    private sealed record AssignedLearner(Guid PersonId, Guid AssignmentId);

    private sealed record RefusedLearner(Guid PersonId, string Code, string Message);

    private sealed record AssignmentRow(Guid LearnerId, Guid CourseId, DateOnly DueDate, int? RecurrenceMonths, Guid AssignedBy, string Status);

    private sealed record Problem(string Code);

    private sealed record EventMetadata(Guid EventIdentifier, DateTimeOffset OccurredAt);

    private sealed record StartedEvent(Guid MembershipId, Guid PersonId, Guid PharmacyId, string DisplayName, string JobFunction, string Role, DateTimeOffset StartedAt, EventMetadata Metadata);

    private sealed record SentChapter(Guid ChapterId, int Position, string Title, string Kind, int? VideoDurationSeconds, string? Text, string? File);

    private sealed record PublishedEvent(Guid CourseId, Guid CourseVersionId, int VersionNumber, string Title, int ValidityMonths, IReadOnlyList<SentChapter> Chapters, EventMetadata Metadata);

    private sealed record RetiredEvent(Guid CourseId, Guid CourseVersionId, EventMetadata Metadata);

    private sealed record Progress(string Status, string TestAccess, DateTimeOffset? StartedAt, DateTimeOffset? TestAccessChangedAt);

    private sealed record SessionEvent(Guid SessionId, Guid PersonId, Guid CourseId, Guid CourseVersionId, EventMetadata Metadata);

    private sealed record ExhaustedEvent(Guid SessionId, Guid PersonId, Guid CourseId, Guid CourseVersionId, IReadOnlyList<Guid> FailedChapterIds, EventMetadata Metadata);

    /// <summary>One learner's session on a course version: its events, each under a new identifier, happened at the instant given.</summary>
    private sealed class Learning(Guid personId, Guid courseId)
    {
        private readonly Guid sessionId = Guid.CreateVersion7();
        private readonly Guid courseVersionId = Guid.CreateVersion7();

        /// <summary>A LearningSessionStarted or a ContentCompleted: the two carry the same fields.</summary>
        public SessionEvent Event(DateTimeOffset occurredAt) =>
            new(sessionId, personId, courseId, courseVersionId, new EventMetadata(Guid.CreateVersion7(), occurredAt));

        public ExhaustedEvent Exhausted(DateTimeOffset occurredAt) =>
            new(sessionId, personId, courseId, courseVersionId, [Guid.CreateVersion7()], new EventMetadata(Guid.CreateVersion7(), occurredAt));
    }
}
