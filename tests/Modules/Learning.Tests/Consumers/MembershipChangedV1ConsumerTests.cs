using Microsoft.Extensions.Logging.Abstractions;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Contracts.Organisation;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.Consumers;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Tests.Consumers;

public class MembershipChangedV1ConsumerTests
{
    [Fact]
    public async Task A_changed_membership_is_stored_with_its_version_person_and_pharmacy_once_the_unit_of_work_commits()
    {
        var copies = new InMemoryMembershipCopies();
        var changed = MembershipEvents.Changed(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), 2);

        await new MembershipChangedV1Consumer(copies, copies, Substitute.For<IPersonalMessagePurge>(), copies, NullLogger<MembershipChangedV1Consumer>.Instance).HandleAsync(changed, TestContext.Current.CancellationToken);

        var change = copies.Changes.ShouldHaveSingleItem();
        (change.MembershipId, change.Version, change.PersonId, change.PharmacyId).ShouldBe((changed.MembershipId, 2, changed.PersonId, changed.PharmacyId));
        copies.Members.ShouldBeEmpty();
        copies.Endings.ShouldBeEmpty();
        copies.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task A_change_delivered_twice_is_stored_once_and_nothing_more_is_committed()
    {
        var copies = new InMemoryMembershipCopies();
        var consumer = new MembershipChangedV1Consumer(copies, copies, Substitute.For<IPersonalMessagePurge>(), copies, NullLogger<MembershipChangedV1Consumer>.Instance);
        var changed = MembershipEvents.Changed(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), 2);

        await consumer.HandleAsync(changed, TestContext.Current.CancellationToken);
        await consumer.HandleAsync(MembershipEvents.Changed(changed.MembershipId, changed.PersonId, changed.PharmacyId, 2), TestContext.Current.CancellationToken);

        copies.Changes.ShouldHaveSingleItem().MembershipId.ShouldBe(changed.MembershipId);
        copies.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task Two_changes_of_one_membership_are_stored_as_two_rows()
    {
        var copies = new InMemoryMembershipCopies();
        var consumer = new MembershipChangedV1Consumer(copies, copies, Substitute.For<IPersonalMessagePurge>(), copies, NullLogger<MembershipChangedV1Consumer>.Instance);
        var membershipId = Guid.CreateVersion7();
        var personId = Guid.CreateVersion7();
        var pharmacyId = Guid.CreateVersion7();

        await consumer.HandleAsync(MembershipEvents.Changed(membershipId, personId, pharmacyId, 2), TestContext.Current.CancellationToken);
        await consumer.HandleAsync(MembershipEvents.Changed(membershipId, personId, pharmacyId, 3), TestContext.Current.CancellationToken);

        copies.Changes.ShouldAllBe(change => change.MembershipId == membershipId);
        copies.Changes.Select(change => change.Version).ShouldBe([2, 3], ignoreOrder: true);
        copies.Commits.ShouldBe(2);
    }

    [Fact]
    public async Task A_change_arriving_before_its_start_is_stored_and_the_later_start_is_stored_too()
    {
        var copies = new InMemoryMembershipCopies();
        var membershipId = Guid.CreateVersion7();
        var personId = Guid.CreateVersion7();
        var pharmacyId = Guid.CreateVersion7();

        await new MembershipChangedV1Consumer(copies, copies, Substitute.For<IPersonalMessagePurge>(), copies, NullLogger<MembershipChangedV1Consumer>.Instance).HandleAsync(MembershipEvents.Changed(membershipId, personId, pharmacyId, 2), TestContext.Current.CancellationToken);
        await new MembershipStartedV1Consumer(copies, copies, Substitute.For<IPersonalMessagePurge>(), copies, NullLogger<MembershipStartedV1Consumer>.Instance).HandleAsync(MembershipEvents.Started(membershipId, personId, pharmacyId), TestContext.Current.CancellationToken);

        copies.Changes.ShouldHaveSingleItem().MembershipId.ShouldBe(membershipId);
        copies.Members.ShouldHaveSingleItem().Id.ShouldBe(membershipId);
        copies.Commits.ShouldBe(2);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task A_version_below_two_is_refused_by_its_name_and_nothing_stored(int version)
    {
        var copies = new InMemoryMembershipCopies();
        var changed = MembershipEvents.Changed(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), version);

        var refused = await Should.ThrowAsync<GuardException>(() => new MembershipChangedV1Consumer(copies, copies, Substitute.For<IPersonalMessagePurge>(), copies, NullLogger<MembershipChangedV1Consumer>.Instance).HandleAsync(changed, TestContext.Current.CancellationToken));

        refused.ParamName.ShouldBe(nameof(MembershipChangedV1.Version));
        copies.Changes.ShouldBeEmpty();
        copies.Commits.ShouldBe(0);
    }

    [Theory]
    [InlineData(nameof(MembershipChangedV1.MembershipId))]
    [InlineData(nameof(MembershipChangedV1.PersonId))]
    [InlineData(nameof(MembershipChangedV1.PharmacyId))]
    public async Task An_empty_id_is_refused_by_its_name_and_nothing_stored(string field)
    {
        var copies = new InMemoryMembershipCopies();
        var changed = MembershipEvents.Changed(
            field == nameof(MembershipChangedV1.MembershipId) ? Guid.Empty : Guid.CreateVersion7(),
            field == nameof(MembershipChangedV1.PersonId) ? Guid.Empty : Guid.CreateVersion7(),
            field == nameof(MembershipChangedV1.PharmacyId) ? Guid.Empty : Guid.CreateVersion7(),
            2);

        var refused = await Should.ThrowAsync<GuardException>(() => new MembershipChangedV1Consumer(copies, copies, Substitute.For<IPersonalMessagePurge>(), copies, NullLogger<MembershipChangedV1Consumer>.Instance).HandleAsync(changed, TestContext.Current.CancellationToken));

        refused.ParamName.ShouldBe(field);
        copies.Changes.ShouldBeEmpty();
        copies.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task No_message_is_refused_and_nothing_stored()
    {
        var copies = new InMemoryMembershipCopies();

        await Should.ThrowAsync<GuardException>(() => new MembershipChangedV1Consumer(copies, copies, Substitute.For<IPersonalMessagePurge>(), copies, NullLogger<MembershipChangedV1Consumer>.Instance).HandleAsync(null!, TestContext.Current.CancellationToken));

        copies.Changes.ShouldBeEmpty();
        copies.Commits.ShouldBe(0);
    }
}
