using System.Globalization;
using SmartSolutionsLab.PharmaTraining.Contracts;
using SmartSolutionsLab.PharmaTraining.Contracts.Certification;
using SmartSolutionsLab.PharmaTraining.Contracts.Compliance;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.Consumers;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Assignments;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Courses;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Members;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Tests.Assignments;

/// <summary>
/// Certification's certificate completes every active assignment of the person for the course, in every pharmacy, and
/// each completion is announced as AssignmentCompletedV1 in the same commit. Idempotent by state: what is completed is no
/// longer active, so the same certificate again finds nothing to do.
/// </summary>
public class CertificateIssuedV1ConsumerTests
{
    private static readonly DateTimeOffset AssignedAt = new(2027, 3, 1, 9, 0, 0, TimeSpan.FromHours(1));
    private static readonly DateTimeOffset IssuedAt = new(2027, 3, 10, 14, 0, 0, TimeSpan.FromHours(1));
    private static readonly DueDate Due = DueDate.From(new DateOnly(2027, 3, 31));
    private static readonly PersonId Manager = PersonId.From(Guid.CreateVersion7());

    [Fact]
    public async Task One_certificate_completes_the_learners_assignments_in_two_pharmacies_and_announces_each()
    {
        var store = new InMemoryAssignmentStore();
        var learner = PersonId.From(Guid.CreateVersion7());
        var course = CourseId.From(Guid.CreateVersion7());
        var inA = Stored(store, learner, PharmacyId.From(Guid.CreateVersion7()), course, AssignedAt);
        var inB = Stored(store, learner, PharmacyId.From(Guid.CreateVersion7()), course, AssignedAt);
        var certificateId = Guid.CreateVersion7();

        await Consumer(store).HandleAsync(CertificateEvents.Issued(certificateId, learner.Value, course.Value, IssuedAt), TestContext.Current.CancellationToken);

        foreach (var assignment in new[] { inA, inB })
        {
            assignment.Status.ShouldBe(AssignmentStatus.Completed);
            assignment.Certificate.ShouldBe(CertificateId.From(certificateId));
            assignment.CompletedAt.ShouldBe(Timestamp.From(IssuedAt));
        }

        var announced = Completions(store);
        announced.Count.ShouldBe(2);
        foreach (var assignment in new[] { inA, inB })
        {
            var completed = announced.Where(each => each.AssignmentId == assignment.Id.Value).ShouldHaveSingleItem();
            completed.PharmacyId.ShouldBe(assignment.Pharmacy.Value);
            completed.PersonId.ShouldBe(learner.Value);
            completed.CourseId.ShouldBe(course.Value);
            completed.CertificateId.ShouldBe(certificateId);
            completed.CompletedAt.ShouldBe(IssuedAt);
            completed.Late.ShouldBeFalse();
        }

        store.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task An_in_progress_assignment_is_completed_like_an_open_one()
    {
        var store = new InMemoryAssignmentStore();
        var learner = PersonId.From(Guid.CreateVersion7());
        var course = CourseId.From(Guid.CreateVersion7());
        var assignment = Stored(store, learner, PharmacyId.From(Guid.CreateVersion7()), course, AssignedAt);
        assignment.MarkStarted(Timestamp.From(AssignedAt.AddDays(2)));
        var certificateId = Guid.CreateVersion7();

        await Consumer(store).HandleAsync(CertificateEvents.Issued(certificateId, learner.Value, course.Value, IssuedAt), TestContext.Current.CancellationToken);

        assignment.Status.ShouldBe(AssignmentStatus.Completed);
        assignment.Certificate.ShouldBe(CertificateId.From(certificateId));
        Completions(store).ShouldHaveSingleItem().AssignmentId.ShouldBe(assignment.Id.Value);
        store.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task A_certificate_issued_before_the_assignment_leaves_it_open_and_announces_nothing()
    {
        var store = new InMemoryAssignmentStore();
        var learner = PersonId.From(Guid.CreateVersion7());
        var course = CourseId.From(Guid.CreateVersion7());
        var assignment = Stored(store, learner, PharmacyId.From(Guid.CreateVersion7()), course, AssignedAt);

        await Consumer(store).HandleAsync(CertificateEvents.Issued(learner.Value, course.Value, AssignedAt.AddMinutes(-1)), TestContext.Current.CancellationToken);

        assignment.Status.ShouldBe(AssignmentStatus.Open);
        assignment.Certificate.ShouldBeNull();
        store.Published.ShouldBeEmpty();
        store.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task Only_the_assignment_made_before_the_certificate_is_completed()
    {
        var store = new InMemoryAssignmentStore();
        var learner = PersonId.From(Guid.CreateVersion7());
        var course = CourseId.From(Guid.CreateVersion7());
        var before = Stored(store, learner, PharmacyId.From(Guid.CreateVersion7()), course, AssignedAt);
        var after = Stored(store, learner, PharmacyId.From(Guid.CreateVersion7()), course, IssuedAt.AddHours(1));

        await Consumer(store).HandleAsync(CertificateEvents.Issued(learner.Value, course.Value, IssuedAt), TestContext.Current.CancellationToken);

        before.Status.ShouldBe(AssignmentStatus.Completed);
        after.Status.ShouldBe(AssignmentStatus.Open);
        after.Certificate.ShouldBeNull();
        var completed = Completions(store).ShouldHaveSingleItem();
        completed.AssignmentId.ShouldBe(before.Id.Value);
        completed.PharmacyId.ShouldBe(before.Pharmacy.Value);
        store.Commits.ShouldBe(1);
    }

    [Theory]
    [InlineData("2027-03-31T21:59:00Z", false)]
    [InlineData("2027-03-31T22:00:00Z", true)]
    [InlineData("2027-03-15T09:00:00Z", false)]
    [InlineData("2027-04-02T09:00:00Z", true)]
    public async Task The_completion_is_late_only_on_a_day_in_Germany_after_the_due_date(string issuedAt, bool late)
    {
        var store = new InMemoryAssignmentStore();
        var learner = PersonId.From(Guid.CreateVersion7());
        var course = CourseId.From(Guid.CreateVersion7());
        Stored(store, learner, PharmacyId.From(Guid.CreateVersion7()), course, AssignedAt);
        var at = DateTimeOffset.Parse(issuedAt, CultureInfo.InvariantCulture);

        await Consumer(store).HandleAsync(CertificateEvents.Issued(learner.Value, course.Value, at), TestContext.Current.CancellationToken);

        var completed = Completions(store).ShouldHaveSingleItem();
        completed.Late.ShouldBe(late);
        completed.CompletedAt.ShouldBe(at);
    }

    [Fact]
    public async Task The_same_certificate_delivered_twice_completes_and_announces_once()
    {
        var store = new InMemoryAssignmentStore();
        var consumer = Consumer(store);
        var learner = PersonId.From(Guid.CreateVersion7());
        var course = CourseId.From(Guid.CreateVersion7());
        var inA = Stored(store, learner, PharmacyId.From(Guid.CreateVersion7()), course, AssignedAt);
        var inB = Stored(store, learner, PharmacyId.From(Guid.CreateVersion7()), course, AssignedAt);
        var issued = CertificateEvents.Issued(learner.Value, course.Value, IssuedAt);

        await consumer.HandleAsync(issued, TestContext.Current.CancellationToken);
        await consumer.HandleAsync(issued, TestContext.Current.CancellationToken);

        Completions(store).Select(each => each.AssignmentId).ShouldBe(new[] { inA.Id.Value, inB.Id.Value }, ignoreOrder: true);
        store.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task The_certificate_published_again_under_a_new_event_identifier_changes_nothing()
    {
        var store = new InMemoryAssignmentStore();
        var consumer = Consumer(store);
        var learner = PersonId.From(Guid.CreateVersion7());
        var course = CourseId.From(Guid.CreateVersion7());
        var assignment = Stored(store, learner, PharmacyId.From(Guid.CreateVersion7()), course, AssignedAt);
        var issued = CertificateEvents.Issued(learner.Value, course.Value, IssuedAt);

        await consumer.HandleAsync(issued, TestContext.Current.CancellationToken);
        await consumer.HandleAsync(issued with { Metadata = new EventMetadata(Guid.CreateVersion7(), IssuedAt) }, TestContext.Current.CancellationToken);

        assignment.Certificate.ShouldBe(CertificateId.From(issued.CertificateId));
        Completions(store).ShouldHaveSingleItem().AssignmentId.ShouldBe(assignment.Id.Value);
        store.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task Another_certificate_for_an_assignment_already_completed_changes_nothing()
    {
        var store = new InMemoryAssignmentStore();
        var consumer = Consumer(store);
        var learner = PersonId.From(Guid.CreateVersion7());
        var course = CourseId.From(Guid.CreateVersion7());
        var assignment = Stored(store, learner, PharmacyId.From(Guid.CreateVersion7()), course, AssignedAt);
        var first = Guid.CreateVersion7();
        var second = Guid.CreateVersion7();

        await consumer.HandleAsync(CertificateEvents.Issued(first, learner.Value, course.Value, IssuedAt), TestContext.Current.CancellationToken);
        await consumer.HandleAsync(CertificateEvents.Issued(second, learner.Value, course.Value, IssuedAt.AddDays(1)), TestContext.Current.CancellationToken);

        assignment.Certificate.ShouldBe(CertificateId.From(first));
        assignment.CompletedAt.ShouldBe(Timestamp.From(IssuedAt));
        Completions(store).ShouldHaveSingleItem().CertificateId.ShouldBe(first);
        store.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task Assignments_of_another_course_another_person_or_cancelled_ones_are_left_alone()
    {
        var store = new InMemoryAssignmentStore();
        var learner = PersonId.From(Guid.CreateVersion7());
        var course = CourseId.From(Guid.CreateVersion7());
        var pharmacy = PharmacyId.From(Guid.CreateVersion7());
        var otherCourse = Stored(store, learner, pharmacy, CourseId.From(Guid.CreateVersion7()), AssignedAt);
        var otherPerson = Stored(store, PersonId.From(Guid.CreateVersion7()), pharmacy, course, AssignedAt);
        var cancelled = Stored(store, learner, PharmacyId.From(Guid.CreateVersion7()), course, AssignedAt);
        cancelled.Cancel(Manager, ChangeReason.From("Versehen"), Timestamp.From(AssignedAt.AddDays(1))).IsSuccess.ShouldBeTrue();
        var completedHere = Stored(store, learner, pharmacy, course, AssignedAt);

        await Consumer(store).HandleAsync(CertificateEvents.Issued(learner.Value, course.Value, IssuedAt), TestContext.Current.CancellationToken);

        otherCourse.Status.ShouldBe(AssignmentStatus.Open);
        otherCourse.Certificate.ShouldBeNull();
        otherPerson.Status.ShouldBe(AssignmentStatus.Open);
        otherPerson.Certificate.ShouldBeNull();
        cancelled.Status.ShouldBe(AssignmentStatus.Cancelled);
        cancelled.Certificate.ShouldBeNull();
        completedHere.Status.ShouldBe(AssignmentStatus.Completed);
        Completions(store).ShouldHaveSingleItem().AssignmentId.ShouldBe(completedHere.Id.Value);
        store.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task A_person_without_an_active_assignment_commits_nothing()
    {
        var store = new InMemoryAssignmentStore();
        var course = CourseId.From(Guid.CreateVersion7());
        Stored(store, PersonId.From(Guid.CreateVersion7()), PharmacyId.From(Guid.CreateVersion7()), course, AssignedAt);

        await Consumer(store).HandleAsync(CertificateEvents.Issued(Guid.CreateVersion7(), course.Value, IssuedAt), TestContext.Current.CancellationToken);

        store.Published.ShouldBeEmpty();
        store.Commits.ShouldBe(0);
    }

    [Theory]
    [InlineData(nameof(CertificateIssuedV1.CertificateId))]
    [InlineData(nameof(CertificateIssuedV1.PersonId))]
    [InlineData(nameof(CertificateIssuedV1.CourseId))]
    [InlineData(nameof(CertificateIssuedV1.CourseVersionId))]
    public async Task An_event_with_an_empty_identifier_is_refused_by_name(string field)
    {
        var store = new InMemoryAssignmentStore();
        var learner = PersonId.From(Guid.CreateVersion7());
        var course = CourseId.From(Guid.CreateVersion7());
        var assignment = Stored(store, learner, PharmacyId.From(Guid.CreateVersion7()), course, AssignedAt);
        var issued = CertificateEvents.Issued(learner.Value, course.Value, IssuedAt);
        var empty = field switch
        {
            nameof(CertificateIssuedV1.CertificateId) => issued with { CertificateId = Guid.Empty },
            nameof(CertificateIssuedV1.PersonId) => issued with { PersonId = Guid.Empty },
            nameof(CertificateIssuedV1.CourseId) => issued with { CourseId = Guid.Empty },
            _ => issued with { CourseVersionId = Guid.Empty },
        };

        var refused = await Should.ThrowAsync<GuardException>(() => Consumer(store).HandleAsync(empty, TestContext.Current.CancellationToken));

        refused.ParamName.ShouldBe(field);
        refused.Message.ShouldContain(field);
        assignment.Status.ShouldBe(AssignmentStatus.Open);
        store.Published.ShouldBeEmpty();
        store.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task An_event_without_an_issue_time_is_refused()
    {
        var store = new InMemoryAssignmentStore();
        var learner = PersonId.From(Guid.CreateVersion7());
        var course = CourseId.From(Guid.CreateVersion7());
        var assignment = Stored(store, learner, PharmacyId.From(Guid.CreateVersion7()), course, AssignedAt);
        var issued = CertificateEvents.Issued(learner.Value, course.Value, IssuedAt) with { IssuedAt = default };

        var refused = await Should.ThrowAsync<GuardException>(() => Consumer(store).HandleAsync(issued, TestContext.Current.CancellationToken));

        refused.ParamName.ShouldBe(nameof(CertificateIssuedV1.IssuedAt));
        assignment.Status.ShouldBe(AssignmentStatus.Open);
        store.Published.ShouldBeEmpty();
        store.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task No_event_at_all_is_refused()
    {
        var store = new InMemoryAssignmentStore();

        await Should.ThrowAsync<GuardException>(() => Consumer(store).HandleAsync(null!, TestContext.Current.CancellationToken));

        store.Commits.ShouldBe(0);
    }

    private static CertificateIssuedV1Consumer Consumer(InMemoryAssignmentStore store) => new(store, store);

    private static Assignment Stored(InMemoryAssignmentStore store, PersonId learner, PharmacyId pharmacy, CourseId course, DateTimeOffset assignedAt)
    {
        var assignment = Assignment.Assign(AssignmentId.New(), learner, pharmacy, course, Due, null, Manager, Timestamp.From(assignedAt)).Value;
        store.Stored.Add(assignment);
        return assignment;
    }

    private static List<AssignmentCompletedV1> Completions(InMemoryAssignmentStore store) =>
        store.Published.OfType<AssignmentCompletedV1>().ToList();
}
