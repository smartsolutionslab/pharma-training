using SmartSolutionsLab.PharmaTraining.Contracts.Compliance;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.Consumers;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Assignments;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Courses;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Members;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Tests.Assignments;

/// <summary>
/// The certificate that completes a recurring assignment requests its follow-up (AssignmentFollowUpRequestedV1) in the
/// commit that completes it, so the request is never lost (#31, docs/adr/0026). The same certificate again, after the
/// follow-up exists, does not complete the follow-up: it was not issued after the follow-up was assigned.
/// </summary>
public class CertificateIssuedV1FollowUpRequestTests
{
    private static readonly DateTimeOffset AssignedAt = new(2027, 3, 1, 9, 0, 0, TimeSpan.FromHours(1));
    private static readonly DateTimeOffset IssuedAt = new(2027, 3, 10, 14, 0, 0, TimeSpan.FromHours(1));
    private static readonly DueDate Due = DueDate.From(new DateOnly(2027, 3, 31));
    private static readonly PersonId Manager = PersonId.From(Guid.CreateVersion7());

    private readonly InMemoryAssignmentStore store = new();
    private readonly PersonId learner = PersonId.From(Guid.CreateVersion7());
    private readonly PharmacyId pharmacy = PharmacyId.From(Guid.CreateVersion7());
    private readonly CourseId course = CourseId.From(Guid.CreateVersion7());

    [Fact]
    public async Task Completing_a_recurring_assignment_requests_its_follow_up_in_the_same_commit()
    {
        var assignment = Stored(RecurrenceInterval.Yearly);

        await Consumer(store).HandleAsync(CertificateEvents.Issued(learner.Value, course.Value, IssuedAt), TestContext.Current.CancellationToken);

        store.Published.OfType<AssignmentCompletedV1>().ShouldHaveSingleItem().AssignmentId.ShouldBe(assignment.Id.Value);
        store.Published.OfType<AssignmentFollowUpRequestedV1>().ShouldHaveSingleItem().AssignmentId.ShouldBe(assignment.Id.Value);
        store.Published.Count.ShouldBe(2);
        store.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task Completing_a_one_off_assignment_requests_no_follow_up()
    {
        var assignment = Stored(null);

        await Consumer(store).HandleAsync(CertificateEvents.Issued(learner.Value, course.Value, IssuedAt), TestContext.Current.CancellationToken);

        store.Published.ShouldHaveSingleItem().ShouldBeOfType<AssignmentCompletedV1>().AssignmentId.ShouldBe(assignment.Id.Value);
        store.Commits.ShouldBe(1);
    }

    /// <summary>
    /// The follow-up was taken at the completion instant, as when Compliance's clock is behind Certification's: on an
    /// assignment made by hand the certificate would fulfil it, which would complete the follow-up and request another.
    /// </summary>
    [Fact]
    public async Task A_redelivered_certificate_after_the_follow_up_exists_requests_nothing_and_leaves_the_follow_up_open()
    {
        var predecessor = Stored(RecurrenceInterval.Yearly);
        var issued = CertificateEvents.Issued(learner.Value, course.Value, IssuedAt);
        predecessor.Complete(CertificateId.From(issued.CertificateId), Timestamp.From(IssuedAt)).IsSuccess.ShouldBeTrue();
        var followUp = predecessor.FollowUp(AssignmentId.New(), Timestamp.From(IssuedAt)).Value;
        store.Stored.Add(followUp);

        await Consumer(store).HandleAsync(issued, TestContext.Current.CancellationToken);

        store.Published.ShouldBeEmpty();
        followUp.Status.ShouldBe(AssignmentStatus.Open);
        followUp.Certificate.ShouldBeNull();
        store.Commits.ShouldBe(0);
    }

    private static CertificateIssuedV1Consumer Consumer(InMemoryAssignmentStore store) => new(store, store);

    private Assignment Stored(RecurrenceInterval? recurrence)
    {
        var assignment = Assignment.Assign(AssignmentId.New(), learner, pharmacy, course, Due, recurrence, Manager, Timestamp.From(AssignedAt)).Value;
        store.Stored.Add(assignment);
        return assignment;
    }
}
