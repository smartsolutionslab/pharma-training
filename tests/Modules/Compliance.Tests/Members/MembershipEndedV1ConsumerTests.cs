using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using SmartSolutionsLab.PharmaTraining.Contracts.Organisation;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.Consumers;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Assignments;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Courses;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Members;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Tests.Assignments;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Tests.Members;

/// <summary>
/// Organisation ended a membership: Compliance stores the ending once and, unless the person still has another
/// membership there that has not ended, cancels their open and in-progress assignments in that pharmacy (#30), by the
/// leaving person, for the reason <see cref="ChangeReason.MembershipEnded"/>, at Compliance's clock. The member store is
/// the unit of work the consumer commits; assignments are seeded straight into the assignment store's stored list, so a
/// change to them is "tracked".
/// </summary>
public class MembershipEndedV1ConsumerTests
{
    private static readonly DateTimeOffset Monday = new(2027, 3, 1, 9, 0, 0, TimeSpan.FromHours(1));
    private static readonly DateTimeOffset Wednesday = Monday.AddDays(2);
    private static readonly DueDate EndOfMarch = DueDate.From(new DateOnly(2027, 3, 31));

    private readonly InMemoryAssignmentStore assignments = new();
    private readonly FakeTimeProvider time = new(Monday);
    private readonly PersonId manager = PersonId.From(Guid.CreateVersion7());
    private readonly PersonId leaver = PersonId.From(Guid.CreateVersion7());
    private readonly PharmacyId pharmacy = PharmacyId.From(Guid.CreateVersion7());

    [Fact]
    public async Task An_ended_membership_is_stored_as_an_ending_once_the_unit_of_work_commits()
    {
        var store = new InMemoryMemberStore();
        var membershipId = Guid.CreateVersion7();
        var personId = Guid.CreateVersion7();
        var pharmacyId = Guid.CreateVersion7();

        await ConsumerOf(store).HandleAsync(MembershipEvents.Ended(membershipId, personId, pharmacyId), TestContext.Current.CancellationToken);

        var ending = store.Endings.ShouldHaveSingleItem();
        ending.Id.ShouldBe(MembershipId.From(membershipId));
        ending.Person.ShouldBe(PersonId.From(personId));
        ending.Pharmacy.ShouldBe(PharmacyId.From(pharmacyId));
        store.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task An_ending_delivered_twice_is_stored_once_and_nothing_more_is_committed()
    {
        var store = new InMemoryMemberStore();
        var consumer = ConsumerOf(store);
        var membershipId = Guid.CreateVersion7();
        var personId = Guid.CreateVersion7();
        var pharmacyId = Guid.CreateVersion7();

        await consumer.HandleAsync(MembershipEvents.Ended(membershipId, personId, pharmacyId), TestContext.Current.CancellationToken);
        await consumer.HandleAsync(MembershipEvents.Ended(membershipId, personId, pharmacyId), TestContext.Current.CancellationToken);

        store.Endings.ShouldHaveSingleItem().Id.ShouldBe(MembershipId.From(membershipId));
        store.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task Ending_a_membership_keeps_its_member()
    {
        var store = new InMemoryMemberStore();
        var started = MembershipEvents.Started(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), "Ada");
        await new MembershipStartedV1Consumer(store, store, Substitute.For<IPersonalMessagePurge>(), store, NullLogger<MembershipStartedV1Consumer>.Instance).HandleAsync(started, TestContext.Current.CancellationToken);

        await ConsumerOf(store).HandleAsync(MembershipEvents.Ended(started.MembershipId, started.PersonId, started.PharmacyId), TestContext.Current.CancellationToken);

        store.Members.ShouldHaveSingleItem().Id.ShouldBe(MembershipId.From(started.MembershipId));
        store.Endings.ShouldHaveSingleItem().Id.ShouldBe(MembershipId.From(started.MembershipId));
    }

    [Fact]
    public async Task An_ending_arriving_before_its_start_is_kept_and_the_later_start_is_stored_too()
    {
        var store = new InMemoryMemberStore();
        var membershipId = Guid.CreateVersion7();
        var personId = Guid.CreateVersion7();
        var pharmacyId = Guid.CreateVersion7();

        await ConsumerOf(store).HandleAsync(MembershipEvents.Ended(membershipId, personId, pharmacyId), TestContext.Current.CancellationToken);
        await new MembershipStartedV1Consumer(store, store, Substitute.For<IPersonalMessagePurge>(), store, NullLogger<MembershipStartedV1Consumer>.Instance).HandleAsync(MembershipEvents.Started(membershipId, personId, pharmacyId, "Ada"), TestContext.Current.CancellationToken);

        store.Endings.ShouldHaveSingleItem().Id.ShouldBe(MembershipId.From(membershipId));
        store.Members.ShouldHaveSingleItem().Id.ShouldBe(MembershipId.From(membershipId));
        (await store.IsEndedAsync(MembershipId.From(membershipId), TestContext.Current.CancellationToken)).ShouldBeTrue();
    }

    [Theory]
    [InlineData(nameof(MembershipEndedV1.MembershipId))]
    [InlineData(nameof(MembershipEndedV1.PersonId))]
    [InlineData(nameof(MembershipEndedV1.PharmacyId))]
    public async Task An_empty_id_is_refused_by_its_name_and_nothing_stored(string field)
    {
        var store = new InMemoryMemberStore();
        var ended = MembershipEvents.Ended(
            field == nameof(MembershipEndedV1.MembershipId) ? Guid.Empty : Guid.CreateVersion7(),
            field == nameof(MembershipEndedV1.PersonId) ? Guid.Empty : Guid.CreateVersion7(),
            field == nameof(MembershipEndedV1.PharmacyId) ? Guid.Empty : Guid.CreateVersion7());

        var refused = await Should.ThrowAsync<GuardException>(() => ConsumerOf(store).HandleAsync(ended, TestContext.Current.CancellationToken));

        refused.ParamName.ShouldBe(field);
        store.Endings.ShouldBeEmpty();
        store.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task No_message_is_refused_and_nothing_stored()
    {
        var store = new InMemoryMemberStore();

        await Should.ThrowAsync<GuardException>(() => ConsumerOf(store).HandleAsync(null!, TestContext.Current.CancellationToken));

        store.Endings.ShouldBeEmpty();
        store.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task Leaving_cancels_the_leavers_open_and_in_progress_assignments_in_that_pharmacy_by_the_leaver_at_compliances_clock()
    {
        var store = new InMemoryMemberStore();
        var membership = await ComplianceMembers.StartAsync(store, leaver, pharmacy, MembershipRole.Employee);
        var open = Seed(leaver, pharmacy);
        var inProgress = Seed(leaver, pharmacy);
        inProgress.MarkStarted(Timestamp.From(Monday.AddHours(1)));
        time.SetUtcNow(Wednesday);

        await ConsumerOf(store).HandleAsync(MembershipEvents.Ended(membership.Value, leaver.Value, pharmacy.Value), TestContext.Current.CancellationToken);

        List<Assignment> active = [open, inProgress];
        foreach (var assignment in active)
        {
            assignment.Status.ShouldBe(AssignmentStatus.Cancelled);
            var change = assignment.History.ShouldHaveSingleItem();
            change.Kind.ShouldBe(AssignmentChangeKind.Cancelled);
            change.ChangedBy.ShouldBe(leaver);
            change.ChangedAt.ShouldBe(Timestamp.From(Wednesday));
            change.PreviousDueDate.ShouldBeNull();
            change.NewDueDate.ShouldBeNull();
            change.Reason.ShouldBe(ChangeReason.MembershipEnded);
        }

        store.Endings.ShouldHaveSingleItem().Id.ShouldBe(membership);
        store.Commits.ShouldBe(2);
    }

    [Fact]
    public async Task Leaving_leaves_a_completed_assignment_as_it_was()
    {
        var store = new InMemoryMemberStore();
        var membership = await ComplianceMembers.StartAsync(store, leaver, pharmacy, MembershipRole.Employee);
        var open = Seed(leaver, pharmacy);
        var completed = Seed(leaver, pharmacy);
        completed.Complete(CertificateId.From(Guid.CreateVersion7()), Timestamp.From(Monday.AddHours(2))).IsSuccess.ShouldBeTrue();

        await ConsumerOf(store).HandleAsync(MembershipEvents.Ended(membership.Value, leaver.Value, pharmacy.Value), TestContext.Current.CancellationToken);

        open.Status.ShouldBe(AssignmentStatus.Cancelled);
        completed.Status.ShouldBe(AssignmentStatus.Completed);
        completed.History.ShouldBeEmpty();
    }

    [Fact]
    public async Task Leaving_one_pharmacy_leaves_the_leavers_assignments_in_another_and_other_learners_assignments_open()
    {
        var store = new InMemoryMemberStore();
        var otherPharmacy = PharmacyId.From(Guid.CreateVersion7());
        var colleague = PersonId.From(Guid.CreateVersion7());
        var membership = await ComplianceMembers.StartAsync(store, leaver, pharmacy, MembershipRole.Employee);
        await ComplianceMembers.StartAsync(store, leaver, otherPharmacy, MembershipRole.Employee);
        await ComplianceMembers.StartAsync(store, colleague, pharmacy, MembershipRole.Employee);
        var here = Seed(leaver, pharmacy);
        var elsewhere = Seed(leaver, otherPharmacy);
        var colleagues = Seed(colleague, pharmacy);

        await ConsumerOf(store).HandleAsync(MembershipEvents.Ended(membership.Value, leaver.Value, pharmacy.Value), TestContext.Current.CancellationToken);

        here.Status.ShouldBe(AssignmentStatus.Cancelled);
        elsewhere.Status.ShouldBe(AssignmentStatus.Open);
        elsewhere.History.ShouldBeEmpty();
        colleagues.Status.ShouldBe(AssignmentStatus.Open);
        colleagues.History.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_ending_delivered_twice_cancels_each_assignment_once_and_stores_one_ending()
    {
        var store = new InMemoryMemberStore();
        var membership = await ComplianceMembers.StartAsync(store, leaver, pharmacy, MembershipRole.Employee);
        var open = Seed(leaver, pharmacy);
        var inProgress = Seed(leaver, pharmacy);
        inProgress.MarkStarted(Timestamp.From(Monday.AddHours(1)));
        var ended = MembershipEvents.Ended(membership.Value, leaver.Value, pharmacy.Value);
        await ConsumerOf(store).HandleAsync(ended, TestContext.Current.CancellationToken);
        time.SetUtcNow(Wednesday);

        await ConsumerOf(store).HandleAsync(ended, TestContext.Current.CancellationToken);

        open.History.ShouldHaveSingleItem().ChangedAt.ShouldBe(Timestamp.From(Monday));
        inProgress.History.ShouldHaveSingleItem().ChangedAt.ShouldBe(Timestamp.From(Monday));
        store.Endings.ShouldHaveSingleItem().Id.ShouldBe(membership);
        store.Commits.ShouldBe(2);
    }

    /// <summary>
    /// An assign that read the person as active before the ending committed may commit after it; the next delivery of
    /// the ending cancels what it left, although the ending is stored by then.
    /// </summary>
    [Fact]
    public async Task A_redelivery_after_the_ending_is_stored_still_cancels_an_assignment_added_in_between()
    {
        var store = new InMemoryMemberStore();
        var membership = await ComplianceMembers.StartAsync(store, leaver, pharmacy, MembershipRole.Employee);
        var ended = MembershipEvents.Ended(membership.Value, leaver.Value, pharmacy.Value);
        await ConsumerOf(store).HandleAsync(ended, TestContext.Current.CancellationToken);
        var latecomer = Seed(leaver, pharmacy);

        await ConsumerOf(store).HandleAsync(ended, TestContext.Current.CancellationToken);

        latecomer.Status.ShouldBe(AssignmentStatus.Cancelled);
        var change = latecomer.History.ShouldHaveSingleItem();
        change.ChangedBy.ShouldBe(leaver);
        change.Reason.ShouldBe(ChangeReason.MembershipEnded);
        store.Endings.ShouldHaveSingleItem().Id.ShouldBe(membership);
        store.Commits.ShouldBe(3);
    }

    /// <summary>
    /// A re-invited person has a new membership; while one of their memberships there has not ended they are a member,
    /// and their assignments stay until the last one ends.
    /// </summary>
    [Fact]
    public async Task Ending_one_of_two_memberships_in_the_pharmacy_cancels_nothing_until_the_other_ends_too()
    {
        var store = new InMemoryMemberStore();
        var first = await ComplianceMembers.StartAsync(store, leaver, pharmacy, MembershipRole.Employee);
        var second = await ComplianceMembers.StartAsync(store, leaver, pharmacy, MembershipRole.Employee);
        var open = Seed(leaver, pharmacy);

        await ConsumerOf(store).HandleAsync(MembershipEvents.Ended(first.Value, leaver.Value, pharmacy.Value), TestContext.Current.CancellationToken);

        open.Status.ShouldBe(AssignmentStatus.Open);
        open.History.ShouldBeEmpty();
        store.Endings.ShouldHaveSingleItem().Id.ShouldBe(first);

        await ConsumerOf(store).HandleAsync(MembershipEvents.Ended(second.Value, leaver.Value, pharmacy.Value), TestContext.Current.CancellationToken);

        open.Status.ShouldBe(AssignmentStatus.Cancelled);
        open.History.ShouldHaveSingleItem().Reason.ShouldBe(ChangeReason.MembershipEnded);
    }

    private MembershipEndedV1Consumer ConsumerOf(InMemoryMemberStore store) => new(store, assignments, store, time);

    /// <summary>An open assignment of a course of its own, stored as if committed earlier.</summary>
    private Assignment Seed(PersonId learner, PharmacyId at)
    {
        var assignment = Assignment.Assign(AssignmentId.New(), learner, at, CourseId.From(Guid.CreateVersion7()), EndOfMarch, null, manager, Timestamp.From(Monday)).Value;
        assignments.Stored.Add(assignment);
        return assignment;
    }
}
