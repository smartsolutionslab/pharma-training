using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using SmartSolutionsLab.PharmaTraining.Contracts;
using SmartSolutionsLab.PharmaTraining.Contracts.Compliance;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.Consumers;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Assignments;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Courses;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Members;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Tests.Members;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Tests.Assignments;

/// <summary>
/// The follow-up of a recurring assignment (#31): Compliance's own request, saved with the completion, makes the next
/// assignment once, due one interval after the completion's day in Germany, assigned by the original assigner and never
/// before the completion. A learner who left the pharmacy, or who already has an active assignment of the course there,
/// gets none; a retired course still gets one (docs/adr/0026).
/// </summary>
public class AssignmentFollowUpRequestedV1ConsumerTests
{
    private static readonly DateTimeOffset AssignedAt = new(2027, 3, 1, 9, 0, 0, TimeSpan.FromHours(1));
    private static readonly DateTimeOffset IssuedAt = new(2027, 3, 10, 14, 0, 0, TimeSpan.FromHours(1));
    private static readonly DueDate Due = DueDate.From(new DateOnly(2027, 3, 31));

    // The reference due date: the completion's day in Germany, 2027-03-10, one year later.
    private static readonly DueDate DueAYearAfterTheCompletion = DueDate.From(new DateOnly(2028, 3, 10));

    private readonly InMemoryAssignmentStore store = new();
    private readonly InMemoryMemberStore members = new();

    // Compliance's clock reads an hour after the certificate was issued.
    private readonly FakeTimeProvider time = new(IssuedAt.AddHours(1));
    private readonly PersonId learner = PersonId.From(Guid.CreateVersion7());
    private readonly PersonId manager = PersonId.From(Guid.CreateVersion7());
    private readonly PharmacyId pharmacy = PharmacyId.From(Guid.CreateVersion7());
    private readonly CourseId course = CourseId.From(Guid.CreateVersion7());

    [Fact]
    public async Task A_completed_yearly_assignment_gets_its_follow_up_due_one_year_after_the_completion_day()
    {
        await MemberAsync();
        var completed = CompletedYearly();

        await Consumer().HandleAsync(Requested(completed.Id.Value), TestContext.Current.CancellationToken);

        var followUp = FollowUps(completed).ShouldHaveSingleItem();
        followUp.Learner.ShouldBe(learner);
        followUp.Pharmacy.ShouldBe(pharmacy);
        followUp.Course.ShouldBe(course);
        followUp.Recurrence.ShouldBe(RecurrenceInterval.Yearly);
        followUp.AssignedBy.ShouldBe(manager);
        followUp.AssignedAt.ShouldBe(Timestamp.From(time.GetUtcNow()));
        followUp.DueDate.ShouldBe(DueAYearAfterTheCompletion);
        followUp.Follows.ShouldBe(completed.Id);
        followUp.Status.ShouldBe(AssignmentStatus.Open);
        followUp.TestAccess.ShouldBe(TestAccess.Available);
        store.Stored.Count.ShouldBe(2);
        var announced = store.Published.ShouldHaveSingleItem().ShouldBeOfType<CourseAssignedV1>();
        announced.AssignmentId.ShouldBe(followUp.Id.Value);
        announced.PersonId.ShouldBe(learner.Value);
        announced.PharmacyId.ShouldBe(pharmacy.Value);
        announced.CourseId.ShouldBe(course.Value);
        announced.DueDate.ShouldBe(DueAYearAfterTheCompletion.Value);
        store.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task A_redelivered_request_creates_no_second_follow_up()
    {
        await MemberAsync();
        var completed = CompletedYearly();
        var consumer = Consumer();
        var requested = Requested(completed.Id.Value);

        await consumer.HandleAsync(requested, TestContext.Current.CancellationToken);
        await consumer.HandleAsync(requested with { Metadata = new EventMetadata(Guid.CreateVersion7(), IssuedAt) }, TestContext.Current.CancellationToken);

        FollowUps(completed).ShouldHaveSingleItem();
        store.Stored.Count.ShouldBe(2);
        store.Published.OfType<CourseAssignedV1>().ShouldHaveSingleItem();
        store.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task A_learner_who_left_the_pharmacy_gets_no_follow_up()
    {
        var membership = await MemberAsync();
        await ComplianceMembers.EndAsync(members, membership, learner, pharmacy);
        var completed = CompletedYearly();

        await Consumer().HandleAsync(Requested(completed.Id.Value), TestContext.Current.CancellationToken);

        store.Stored.ShouldHaveSingleItem().ShouldBeSameAs(completed);
        store.Published.ShouldBeEmpty();
        store.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task A_learner_with_an_active_assignment_of_the_course_gets_no_follow_up()
    {
        await MemberAsync();
        var completed = CompletedYearly();

        // A manager assigned the course again after the completion, before the request was handled.
        var assignedAgain = Assignment.Assign(AssignmentId.New(), learner, pharmacy, course, DueAYearAfterTheCompletion, null, manager, Timestamp.From(IssuedAt.AddMinutes(30))).Value;
        store.Stored.Add(assignedAgain);

        await Consumer().HandleAsync(Requested(completed.Id.Value), TestContext.Current.CancellationToken);

        store.Stored.ShouldBe([completed, assignedAgain], ignoreOrder: true);
        FollowUps(completed).ShouldBeEmpty();
        store.Published.ShouldBeEmpty();
        store.Commits.ShouldBe(0);
    }

    /// <summary>
    /// A decision guard (owner default on #31): the consumer asks no course store, so a course retired before its next
    /// version is published cannot end the recurrence; the follow-up shows as open, and later as overdue, in the matrix.
    /// </summary>
    [Fact]
    public async Task A_retired_course_still_gets_its_follow_up()
    {
        await MemberAsync();
        var completed = CompletedYearly();

        await Consumer().HandleAsync(Requested(completed.Id.Value), TestContext.Current.CancellationToken);

        FollowUps(completed).ShouldHaveSingleItem().Status.ShouldBe(AssignmentStatus.Open);
        store.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task An_unknown_assignment_throws_so_the_request_is_retried()
    {
        await MemberAsync();

        await Should.ThrowAsync<InvalidOperationException>(() => Consumer().HandleAsync(Requested(Guid.CreateVersion7()), TestContext.Current.CancellationToken));

        store.Stored.ShouldBeEmpty();
        store.Published.ShouldBeEmpty();
        store.Commits.ShouldBe(0);
    }

    /// <summary>
    /// Only a completed recurring assignment is requested a follow-up, so anything else is a bug or a forged request.
    /// An open recurring assignment is not among the cases: it is itself the learner's active assignment of the course.
    /// </summary>
    [Theory]
    [InlineData("one-off")]
    [InlineData("cancelled")]
    public async Task A_one_off_or_uncompleted_assignment_throws(string kind)
    {
        await MemberAsync();
        var assignment = kind switch
        {
            "one-off" => Completed(null),
            _ => Cancelled(),
        };

        await Should.ThrowAsync<InvalidOperationException>(() => Consumer().HandleAsync(Requested(assignment.Id.Value), TestContext.Current.CancellationToken));

        store.Stored.ShouldHaveSingleItem().ShouldBeSameAs(assignment);
        store.Published.ShouldBeEmpty();
        store.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task An_empty_assignment_id_is_refused()
    {
        await MemberAsync();
        CompletedYearly();

        await Should.ThrowAsync<GuardException>(() => Consumer().HandleAsync(Requested(Guid.Empty), TestContext.Current.CancellationToken));

        store.Stored.Count.ShouldBe(1);
        store.Published.ShouldBeEmpty();
        store.Commits.ShouldBe(0);
    }

    private static AssignmentFollowUpRequestedV1 Requested(Guid assignment) =>
        new(assignment) { Metadata = new EventMetadata(Guid.CreateVersion7(), IssuedAt) };

    private AssignmentFollowUpRequestedV1Consumer Consumer() =>
        new(store, members, store, time, NullLogger<AssignmentFollowUpRequestedV1Consumer>.Instance);

    private Task<MembershipId> MemberAsync() => ComplianceMembers.StartAsync(members, learner, pharmacy, MembershipRole.Employee);

    private Assignment CompletedYearly() => Completed(RecurrenceInterval.Yearly);

    private Assignment Completed(RecurrenceInterval? recurrence)
    {
        var assignment = Assignment.Assign(AssignmentId.New(), learner, pharmacy, course, Due, recurrence, manager, Timestamp.From(AssignedAt)).Value;
        assignment.Complete(CertificateId.From(Guid.CreateVersion7()), Timestamp.From(IssuedAt)).IsSuccess.ShouldBeTrue();
        store.Stored.Add(assignment);
        return assignment;
    }

    private Assignment Cancelled()
    {
        var assignment = Assignment.Assign(AssignmentId.New(), learner, pharmacy, course, Due, RecurrenceInterval.Yearly, manager, Timestamp.From(AssignedAt)).Value;
        assignment.Cancel(manager, ChangeReason.From("Versehen"), Timestamp.From(IssuedAt)).IsSuccess.ShouldBeTrue();
        store.Stored.Add(assignment);
        return assignment;
    }

    private List<Assignment> FollowUps(Assignment completed) =>
        store.Stored.Where(stored => stored.Follows == completed.Id).ToList();
}
