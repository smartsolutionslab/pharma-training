using Microsoft.Extensions.Logging.Abstractions;
using SmartSolutionsLab.PharmaTraining.Contracts.Organisation;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Application.Consumers;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Certificates;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Memberships;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Tests.Erasures;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Tests.Memberships;

/// <summary>
/// Certification's copy of a change Organisation made to a membership (docs/adr/0015): stored once per membership and
/// version, beside the membership and whether or not it has arrived yet, keeping the role and nothing else.
/// </summary>
public class MembershipChangedV1ConsumerTests
{
    [Fact]
    public async Task A_changed_membership_is_stored_as_a_change_with_its_role_once_the_unit_of_work_commits()
    {
        var store = new InMemoryMembershipStore();
        var membershipId = Guid.CreateVersion7();
        var personId = Guid.CreateVersion7();
        var pharmacyId = Guid.CreateVersion7();

        await new MembershipChangedV1Consumer(store, store).HandleAsync(MembershipEvents.Changed(membershipId, personId, pharmacyId, MembershipRoleV1.Manager, 2), TestContext.Current.CancellationToken);

        var change = store.Changes.ShouldHaveSingleItem();
        change.Membership.ShouldBe(MembershipId.From(membershipId));
        change.Person.ShouldBe(PersonId.From(personId));
        change.Pharmacy.ShouldBe(PharmacyId.From(pharmacyId));
        change.Version.ShouldBe(MembershipVersion.From(2));
        change.Role.ShouldBe(MembershipRole.Manager);
        store.Memberships.ShouldBeEmpty();
        store.Endings.ShouldBeEmpty();
        store.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task A_change_delivered_twice_is_stored_once_as_it_first_arrived_and_nothing_more_is_committed()
    {
        var store = new InMemoryMembershipStore();
        var consumer = new MembershipChangedV1Consumer(store, store);
        var first = MembershipEvents.Changed(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), MembershipRoleV1.Manager, 2);

        await consumer.HandleAsync(first, TestContext.Current.CancellationToken);
        await consumer.HandleAsync(
            MembershipEvents.Changed(first.MembershipId, first.PersonId, first.PharmacyId, MembershipRoleV1.Employee, 2),
            TestContext.Current.CancellationToken);

        store.Changes.ShouldHaveSingleItem().Role.ShouldBe(MembershipRole.Manager);
        store.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task Two_changes_of_one_membership_are_stored_as_two_rows()
    {
        var store = new InMemoryMembershipStore();
        var consumer = new MembershipChangedV1Consumer(store, store);
        var membershipId = Guid.CreateVersion7();
        var personId = Guid.CreateVersion7();
        var pharmacyId = Guid.CreateVersion7();

        await consumer.HandleAsync(MembershipEvents.Changed(membershipId, personId, pharmacyId, MembershipRoleV1.Manager, 2), TestContext.Current.CancellationToken);
        await consumer.HandleAsync(MembershipEvents.Changed(membershipId, personId, pharmacyId, MembershipRoleV1.Employee, 3), TestContext.Current.CancellationToken);

        store.Changes.Count.ShouldBe(2);
        store.Changes.Select(change => change.Version).ShouldBe([MembershipVersion.From(2), MembershipVersion.From(3)], ignoreOrder: true);
        store.Commits.ShouldBe(2);
    }

    [Fact]
    public async Task A_change_arriving_before_its_start_is_stored_and_the_later_start_is_stored_too()
    {
        var store = new InMemoryMembershipStore();
        var membershipId = Guid.CreateVersion7();
        var personId = Guid.CreateVersion7();
        var pharmacyId = Guid.CreateVersion7();

        await new MembershipChangedV1Consumer(store, store).HandleAsync(MembershipEvents.Changed(membershipId, personId, pharmacyId, MembershipRoleV1.Manager, 2), TestContext.Current.CancellationToken);
        await new MembershipStartedV1Consumer(store, new InMemoryErasedPersonStore(), store, NullLogger<MembershipStartedV1Consumer>.Instance).HandleAsync(MembershipEvents.Started(membershipId, personId, pharmacyId, "Ada"), TestContext.Current.CancellationToken);

        store.Changes.ShouldHaveSingleItem().Membership.ShouldBe(MembershipId.From(membershipId));
        var membership = store.Memberships.ShouldHaveSingleItem();
        membership.Role.ShouldBe(MembershipRole.Employee);
        membership.CurrentRole(store.Changes).ShouldBe(MembershipRole.Manager);
    }

    [Fact]
    public async Task A_name_or_job_function_certification_does_not_keep_is_not_checked()
    {
        var store = new InMemoryMembershipStore();
        var changed = MembershipEvents.Changed(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), MembershipRoleV1.Manager, 2) with
        {
            DisplayName = " ",
            JobFunction = (JobFunctionV1)99,
        };

        await new MembershipChangedV1Consumer(store, store).HandleAsync(changed, TestContext.Current.CancellationToken);

        store.Changes.ShouldHaveSingleItem();
    }

    [Theory]
    [InlineData(nameof(MembershipChangedV1.MembershipId))]
    [InlineData(nameof(MembershipChangedV1.PersonId))]
    [InlineData(nameof(MembershipChangedV1.PharmacyId))]
    public async Task An_empty_id_is_refused_by_its_name_and_nothing_stored(string field)
    {
        var changed = MembershipEvents.Changed(
            field == nameof(MembershipChangedV1.MembershipId) ? Guid.Empty : Guid.CreateVersion7(),
            field == nameof(MembershipChangedV1.PersonId) ? Guid.Empty : Guid.CreateVersion7(),
            field == nameof(MembershipChangedV1.PharmacyId) ? Guid.Empty : Guid.CreateVersion7(),
            MembershipRoleV1.Manager,
            2);

        await RefusedAsync(changed, field);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task A_version_below_two_is_refused_by_its_name_and_nothing_stored(int version)
    {
        var changed = MembershipEvents.Changed(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), MembershipRoleV1.Manager, version);

        await RefusedAsync(changed, nameof(MembershipChangedV1.Version));
    }

    [Fact]
    public async Task An_undefined_role_is_refused_by_its_name_and_nothing_stored()
    {
        var changed = MembershipEvents.Changed(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), (MembershipRoleV1)99, 2);

        await RefusedAsync(changed, nameof(MembershipChangedV1.Role));
    }

    [Fact]
    public async Task No_message_is_refused_and_nothing_stored()
    {
        var store = new InMemoryMembershipStore();

        await Should.ThrowAsync<GuardException>(() => new MembershipChangedV1Consumer(store, store).HandleAsync(null!, TestContext.Current.CancellationToken));

        store.Changes.ShouldBeEmpty();
        store.Commits.ShouldBe(0);
    }

    private static async Task RefusedAsync(MembershipChangedV1 changed, string field)
    {
        var store = new InMemoryMembershipStore();

        var refused = await Should.ThrowAsync<GuardException>(() => new MembershipChangedV1Consumer(store, store).HandleAsync(changed, TestContext.Current.CancellationToken));

        refused.ParamName.ShouldBe(field);
        store.Changes.ShouldBeEmpty();
        store.Commits.ShouldBe(0);
    }
}
