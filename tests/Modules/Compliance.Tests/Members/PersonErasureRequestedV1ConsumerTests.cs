using Microsoft.Extensions.Time.Testing;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Contracts.Compliance;
using SmartSolutionsLab.PharmaTraining.Contracts.Organisation;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.Consumers;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Assignments;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Courses;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Members;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Tests.Assignments;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Tests.Members;

/// <summary>
/// Organisation announced a person's erasure (#115): Compliance tombstones the person, forgets their name on every copy
/// of their memberships and changes, cancels their open and in-progress assignments in every pharmacy as
/// <see cref="MembershipEndedV1Consumer"/> does, purges the stored messages that carry their name and announces
/// <see cref="ComplianceErasureCompletedV1"/>. It is idempotent by state: a redelivery changes nothing but announces
/// again, and an unknown person completes too. The member store is the unit of work the consumer commits; assignments
/// are seeded straight into the assignment store's stored list, so a change to them is "tracked".
/// </summary>
public class PersonErasureRequestedV1ConsumerTests
{
    private static readonly DateTimeOffset Monday = new(2027, 3, 1, 9, 0, 0, TimeSpan.FromHours(1));
    private static readonly DateTimeOffset Wednesday = Monday.AddDays(2);
    private static readonly DueDate EndOfMarch = DueDate.From(new DateOnly(2027, 3, 31));

    private readonly InMemoryMemberStore store = new();
    private readonly InMemoryAssignmentStore assignments = new();
    private readonly IPersonalMessagePurge purge = Substitute.For<IPersonalMessagePurge>();
    private readonly FakeTimeProvider time = new(Monday);
    private readonly PersonId manager = PersonId.From(Guid.CreateVersion7());
    private readonly PersonId person = PersonId.From(Guid.CreateVersion7());
    private readonly PersonId colleague = PersonId.From(Guid.CreateVersion7());
    private readonly PharmacyId pharmacy = PharmacyId.From(Guid.CreateVersion7());
    private readonly PharmacyId otherPharmacy = PharmacyId.From(Guid.CreateVersion7());
    private readonly Guid erasure = Guid.CreateVersion7();

    [Fact]
    public async Task The_name_is_erased_from_every_member_and_change_of_the_person_and_kept_for_everyone_else()
    {
        var here = await ComplianceMembers.StartAsync(store, person, pharmacy, MembershipRole.Employee, "Ada Lovelace");
        await ComplianceMembers.ChangeAsync(store, here, person, pharmacy, 2, MembershipRole.Manager, "Ada Byron");
        var there = await ComplianceMembers.StartAsync(store, person, otherPharmacy, MembershipRole.Employee, "Ada Lovelace");
        await ComplianceMembers.EndAsync(store, there, person, otherPharmacy);
        await ComplianceMembers.StartAsync(store, colleague, pharmacy, MembershipRole.Employee, "Grace Hopper");

        await Consumer().HandleAsync(Requested(person), TestContext.Current.CancellationToken);

        store.Members.Where(member => member.Person == person).ShouldAllBe(member => member.Name == null);
        store.Members.Count(member => member.Person == person).ShouldBe(2);
        store.Changes.ShouldHaveSingleItem().Name.ShouldBeNull();
        store.Members.Single(member => member.Person == colleague).Name.ShouldBe(MemberName.From("Grace Hopper"));
    }

    [Fact]
    public async Task The_person_is_tombstoned_at_compliances_clock()
    {
        await ComplianceMembers.StartAsync(store, person, pharmacy, MembershipRole.Employee);
        time.SetUtcNow(Wednesday);

        await Consumer().HandleAsync(Requested(person), TestContext.Current.CancellationToken);

        var tombstone = store.ErasedPersons.ShouldHaveSingleItem();
        tombstone.Person.ShouldBe(person);
        tombstone.ErasedAt.ShouldBe(Timestamp.From(Wednesday));
    }

    [Fact]
    public async Task Open_and_in_progress_assignments_in_every_pharmacy_are_cancelled_by_the_person_as_their_membership_ended()
    {
        await ComplianceMembers.StartAsync(store, person, pharmacy, MembershipRole.Employee);
        await ComplianceMembers.StartAsync(store, person, otherPharmacy, MembershipRole.Employee);
        var open = Seed(person, pharmacy);
        var inProgress = Seed(person, otherPharmacy);
        inProgress.MarkStarted(Timestamp.From(Monday.AddHours(1)));
        time.SetUtcNow(Wednesday);

        await Consumer().HandleAsync(Requested(person), TestContext.Current.CancellationToken);

        List<Assignment> active = [open, inProgress];
        foreach (var assignment in active)
        {
            assignment.Status.ShouldBe(AssignmentStatus.Cancelled);
            var change = assignment.History.ShouldHaveSingleItem();
            change.Kind.ShouldBe(AssignmentChangeKind.Cancelled);
            change.ChangedBy.ShouldBe(person);
            change.ChangedAt.ShouldBe(Timestamp.From(Wednesday));
            change.Reason.ShouldBe(ChangeReason.MembershipEnded);
        }
    }

    [Fact]
    public async Task A_completed_assignment_and_another_learners_assignments_are_left_as_they_were()
    {
        await ComplianceMembers.StartAsync(store, person, pharmacy, MembershipRole.Employee);
        var completed = Seed(person, pharmacy);
        completed.Complete(CertificateId.From(Guid.CreateVersion7()), Timestamp.From(Monday.AddHours(2))).IsSuccess.ShouldBeTrue();
        var colleagues = Seed(colleague, pharmacy);

        await Consumer().HandleAsync(Requested(person), TestContext.Current.CancellationToken);

        completed.Status.ShouldBe(AssignmentStatus.Completed);
        completed.History.ShouldBeEmpty();
        colleagues.Status.ShouldBe(AssignmentStatus.Open);
        colleagues.History.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_erasure_holds_the_person_before_it_reads_anything_of_theirs()
    {
        await Consumer().HandleAsync(Requested(person), TestContext.Current.CancellationToken);

        store.PersonCalls.ShouldBe(["hold", "is-erased"], "a membership event running at the same time must not commit the name between this read and the tombstone");
    }

    [Fact]
    public async Task The_stored_membership_messages_holding_the_persons_id_are_purged()
    {
        await ComplianceMembers.StartAsync(store, person, pharmacy, MembershipRole.Employee);

        await Consumer().HandleAsync(Requested(person), TestContext.Current.CancellationToken);

        await purge.Received(1).PurgeAsync(
            person.Value,
            Arg.Is<IReadOnlyCollection<Type>>(types => types.Count == 2 && types.Contains(typeof(MembershipStartedV1)) && types.Contains(typeof(MembershipChangedV1))),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Compliances_completion_is_announced_with_the_erasure_once_committed()
    {
        await ComplianceMembers.StartAsync(store, person, pharmacy, MembershipRole.Employee);
        var commitsBefore = store.Commits;

        await Consumer().HandleAsync(Requested(person), TestContext.Current.CancellationToken);

        store.Published.ShouldHaveSingleItem().ShouldBe(new ComplianceErasureCompletedV1(erasure));
        store.Commits.ShouldBe(commitsBefore + 1);
    }

    [Fact]
    public async Task An_unknown_person_still_completes_and_changes_no_one_else()
    {
        await ComplianceMembers.StartAsync(store, colleague, pharmacy, MembershipRole.Employee, "Grace Hopper");
        var colleagues = Seed(colleague, pharmacy);

        await Consumer().HandleAsync(Requested(person), TestContext.Current.CancellationToken);

        store.Published.ShouldHaveSingleItem().ShouldBe(new ComplianceErasureCompletedV1(erasure));
        store.Members.ShouldHaveSingleItem().Name.ShouldBe(MemberName.From("Grace Hopper"));
        colleagues.Status.ShouldBe(AssignmentStatus.Open);
    }

    [Fact]
    public async Task A_redelivery_changes_nothing_but_announces_the_completion_again()
    {
        await ComplianceMembers.StartAsync(store, person, pharmacy, MembershipRole.Employee);
        var open = Seed(person, pharmacy);
        var requested = Requested(person);
        await Consumer().HandleAsync(requested, TestContext.Current.CancellationToken);
        time.SetUtcNow(Wednesday);

        await Consumer().HandleAsync(requested, TestContext.Current.CancellationToken);

        store.ErasedPersons.ShouldHaveSingleItem().ErasedAt.ShouldBe(Timestamp.From(Monday), "the first tombstone stays as it was");
        open.History.ShouldHaveSingleItem().ChangedAt.ShouldBe(Timestamp.From(Monday));
        store.Members.ShouldHaveSingleItem().Name.ShouldBeNull();
        store.Published.Count.ShouldBe(2);
        store.Published.ShouldAllBe(published => published.Equals(new ComplianceErasureCompletedV1(erasure)));
    }

    [Theory]
    [InlineData(nameof(PersonErasureRequestedV1.ErasureId))]
    [InlineData(nameof(PersonErasureRequestedV1.PersonId))]
    public async Task An_empty_id_is_refused_by_its_name_and_nothing_committed(string field)
    {
        var requested = new PersonErasureRequestedV1(
            field == nameof(PersonErasureRequestedV1.ErasureId) ? Guid.Empty : erasure,
            field == nameof(PersonErasureRequestedV1.PersonId) ? Guid.Empty : person.Value);

        var refused = await Should.ThrowAsync<GuardException>(() => Consumer().HandleAsync(requested, TestContext.Current.CancellationToken));

        refused.ParamName.ShouldBe(field);
        store.Commits.ShouldBe(0);
        store.Published.ShouldBeEmpty();
    }

    [Fact]
    public async Task No_message_is_refused_and_nothing_committed()
    {
        await Should.ThrowAsync<GuardException>(() => Consumer().HandleAsync(null!, TestContext.Current.CancellationToken));

        store.Commits.ShouldBe(0);
        store.Published.ShouldBeEmpty();
    }

    private PersonErasureRequestedV1Consumer Consumer() => new(store, store, assignments, purge, store, time);

    private PersonErasureRequestedV1 Requested(PersonId erased) => new(erasure, erased.Value);

    /// <summary>An open assignment of a course of its own, stored as if committed earlier.</summary>
    private Assignment Seed(PersonId learner, PharmacyId at)
    {
        var assignment = Assignment.Assign(AssignmentId.New(), learner, at, CourseId.From(Guid.CreateVersion7()), EndOfMarch, null, manager, Timestamp.From(Monday)).Value;
        assignments.Stored.Add(assignment);
        return assignment;
    }
}
