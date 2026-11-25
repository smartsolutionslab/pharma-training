using Microsoft.Extensions.Logging.Abstractions;
using SmartSolutionsLab.PharmaTraining.Contracts.Organisation;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.Consumers;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Members;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Tests.Members;

public class MembershipChangedV1ConsumerTests
{
    [Fact]
    public async Task A_changed_membership_is_stored_with_every_field_once_the_unit_of_work_commits()
    {
        var store = new InMemoryMemberStore();
        var membershipId = Guid.CreateVersion7();
        var personId = Guid.CreateVersion7();
        var pharmacyId = Guid.CreateVersion7();
        var changed = MembershipEvents.Changed(membershipId, personId, pharmacyId, "Ada Byron", JobFunctionV1.Pka, MembershipRoleV1.Manager, 2);

        await new MembershipChangedV1Consumer(store, store, store, NullLogger<MembershipChangedV1Consumer>.Instance).HandleAsync(changed, TestContext.Current.CancellationToken);

        var change = store.Changes.ShouldHaveSingleItem();
        change.Membership.ShouldBe(MembershipId.From(membershipId));
        change.Person.ShouldBe(PersonId.From(personId));
        change.Pharmacy.ShouldBe(PharmacyId.From(pharmacyId));
        change.Version.ShouldBe(MembershipVersion.From(2));
        change.Name.ShouldBe(MemberName.From("Ada Byron"));
        change.JobFunction.ShouldBe(JobFunction.Pka);
        change.Role.ShouldBe(MembershipRole.Manager);
        store.Members.ShouldBeEmpty();
        store.Endings.ShouldBeEmpty();
        store.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task A_change_delivered_twice_is_stored_once_and_nothing_more_is_committed()
    {
        var store = new InMemoryMemberStore();
        var consumer = new MembershipChangedV1Consumer(store, store, store, NullLogger<MembershipChangedV1Consumer>.Instance);
        var first = MembershipEvents.Changed(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), "Ada Byron", JobFunctionV1.Pka, MembershipRoleV1.Manager, 2);

        await consumer.HandleAsync(first, TestContext.Current.CancellationToken);
        await consumer.HandleAsync(
            MembershipEvents.Changed(first.MembershipId, first.PersonId, first.PharmacyId, "Ada Lovelace", JobFunctionV1.Other, MembershipRoleV1.Employee, 2),
            TestContext.Current.CancellationToken);

        var change = store.Changes.ShouldHaveSingleItem();
        change.Name.ShouldBe(MemberName.From("Ada Byron"));
        change.Role.ShouldBe(MembershipRole.Manager);
        store.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task Two_changes_of_one_membership_are_stored_as_two_rows()
    {
        var store = new InMemoryMemberStore();
        var consumer = new MembershipChangedV1Consumer(store, store, store, NullLogger<MembershipChangedV1Consumer>.Instance);
        var membershipId = Guid.CreateVersion7();
        var personId = Guid.CreateVersion7();
        var pharmacyId = Guid.CreateVersion7();

        await consumer.HandleAsync(MembershipEvents.Changed(membershipId, personId, pharmacyId, "Ada", JobFunctionV1.Pta, MembershipRoleV1.Manager, 2), TestContext.Current.CancellationToken);
        await consumer.HandleAsync(MembershipEvents.Changed(membershipId, personId, pharmacyId, "Ada", JobFunctionV1.Pta, MembershipRoleV1.Employee, 3), TestContext.Current.CancellationToken);

        store.Changes.Count.ShouldBe(2);
        store.Changes.ShouldAllBe(change => change.Membership == MembershipId.From(membershipId));
        store.Changes.Select(change => change.Version).ShouldBe([MembershipVersion.From(2), MembershipVersion.From(3)], ignoreOrder: true);
        store.Commits.ShouldBe(2);
    }

    [Fact]
    public async Task A_change_arriving_before_its_start_is_stored_and_the_later_start_is_stored_too()
    {
        var store = new InMemoryMemberStore();
        var membershipId = Guid.CreateVersion7();
        var personId = Guid.CreateVersion7();
        var pharmacyId = Guid.CreateVersion7();

        await new MembershipChangedV1Consumer(store, store, store, NullLogger<MembershipChangedV1Consumer>.Instance).HandleAsync(
            MembershipEvents.Changed(membershipId, personId, pharmacyId, "Ada", JobFunctionV1.Pta, MembershipRoleV1.Manager, 2),
            TestContext.Current.CancellationToken);
        await new MembershipStartedV1Consumer(store, store, store, NullLogger<MembershipStartedV1Consumer>.Instance).HandleAsync(
            MembershipEvents.Started(membershipId, personId, pharmacyId, "Ada"),
            TestContext.Current.CancellationToken);

        store.Changes.ShouldHaveSingleItem().Membership.ShouldBe(MembershipId.From(membershipId));
        var member = store.Members.ShouldHaveSingleItem();
        member.Id.ShouldBe(MembershipId.From(membershipId));
        member.Role.ShouldBe(MembershipRole.Employee);
        store.Commits.ShouldBe(2);
    }

    [Theory]
    [InlineData(nameof(MembershipChangedV1.MembershipId))]
    [InlineData(nameof(MembershipChangedV1.PersonId))]
    [InlineData(nameof(MembershipChangedV1.PharmacyId))]
    public async Task An_empty_id_is_refused_by_its_name_and_nothing_stored(string field)
    {
        var store = new InMemoryMemberStore();
        var changed = MembershipEvents.Changed(
            field == nameof(MembershipChangedV1.MembershipId) ? Guid.Empty : Guid.CreateVersion7(),
            field == nameof(MembershipChangedV1.PersonId) ? Guid.Empty : Guid.CreateVersion7(),
            field == nameof(MembershipChangedV1.PharmacyId) ? Guid.Empty : Guid.CreateVersion7(),
            "Ada",
            JobFunctionV1.Pta,
            MembershipRoleV1.Manager,
            2);

        var refused = await Should.ThrowAsync<GuardException>(() => new MembershipChangedV1Consumer(store, store, store, NullLogger<MembershipChangedV1Consumer>.Instance).HandleAsync(changed, TestContext.Current.CancellationToken));

        refused.ParamName.ShouldBe(field);
        store.Changes.ShouldBeEmpty();
        store.Commits.ShouldBe(0);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(0)]
    public async Task A_version_below_two_is_refused_by_its_name_and_nothing_stored(int version)
    {
        var store = new InMemoryMemberStore();
        var changed = MembershipEvents.Changed(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), "Ada", JobFunctionV1.Pta, MembershipRoleV1.Manager, version);

        var refused = await Should.ThrowAsync<GuardException>(() => new MembershipChangedV1Consumer(store, store, store, NullLogger<MembershipChangedV1Consumer>.Instance).HandleAsync(changed, TestContext.Current.CancellationToken));

        refused.ParamName.ShouldBe(nameof(MembershipChangedV1.Version));
        store.Changes.ShouldBeEmpty();
        store.Commits.ShouldBe(0);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Ada\0Lovelace")]
    public async Task A_missing_or_blank_display_name_or_one_holding_a_nul_character_is_refused_by_its_name_and_nothing_stored(string? displayName)
    {
        var store = new InMemoryMemberStore();
        var changed = MembershipEvents.Changed(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), displayName!, JobFunctionV1.Pta, MembershipRoleV1.Manager, 2);

        var refused = await Should.ThrowAsync<GuardException>(() => new MembershipChangedV1Consumer(store, store, store, NullLogger<MembershipChangedV1Consumer>.Instance).HandleAsync(changed, TestContext.Current.CancellationToken));

        refused.ParamName.ShouldBe(nameof(MembershipChangedV1.DisplayName));
        store.Changes.ShouldBeEmpty();
        store.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task A_display_name_longer_than_organisation_allows_is_refused_by_its_name_and_nothing_stored()
    {
        var store = new InMemoryMemberStore();
        var changed = MembershipEvents.Changed(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), new string('a', 101), JobFunctionV1.Pta, MembershipRoleV1.Manager, 2);

        var refused = await Should.ThrowAsync<GuardException>(() => new MembershipChangedV1Consumer(store, store, store, NullLogger<MembershipChangedV1Consumer>.Instance).HandleAsync(changed, TestContext.Current.CancellationToken));

        refused.ParamName.ShouldBe(nameof(MembershipChangedV1.DisplayName));
        store.Changes.ShouldBeEmpty();
        store.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task An_undefined_role_is_refused_by_its_name_and_nothing_stored()
    {
        var store = new InMemoryMemberStore();
        var changed = MembershipEvents.Changed(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), "Ada", JobFunctionV1.Pta, (MembershipRoleV1)99, 2);

        var refused = await Should.ThrowAsync<GuardException>(() => new MembershipChangedV1Consumer(store, store, store, NullLogger<MembershipChangedV1Consumer>.Instance).HandleAsync(changed, TestContext.Current.CancellationToken));

        refused.ParamName.ShouldBe(nameof(MembershipChangedV1.Role));
        store.Changes.ShouldBeEmpty();
        store.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task An_undefined_job_function_is_refused_by_its_name_and_nothing_stored()
    {
        var store = new InMemoryMemberStore();
        var changed = MembershipEvents.Changed(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), "Ada", (JobFunctionV1)99, MembershipRoleV1.Manager, 2);

        var refused = await Should.ThrowAsync<GuardException>(() => new MembershipChangedV1Consumer(store, store, store, NullLogger<MembershipChangedV1Consumer>.Instance).HandleAsync(changed, TestContext.Current.CancellationToken));

        refused.ParamName.ShouldBe(nameof(MembershipChangedV1.JobFunction));
        store.Changes.ShouldBeEmpty();
        store.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task No_message_is_refused_and_nothing_stored()
    {
        var store = new InMemoryMemberStore();

        await Should.ThrowAsync<GuardException>(() => new MembershipChangedV1Consumer(store, store, store, NullLogger<MembershipChangedV1Consumer>.Instance).HandleAsync(null!, TestContext.Current.CancellationToken));

        store.Changes.ShouldBeEmpty();
        store.Commits.ShouldBe(0);
    }
}
