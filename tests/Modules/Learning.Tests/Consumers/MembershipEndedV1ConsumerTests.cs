using Microsoft.Extensions.Logging.Abstractions;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Contracts.Organisation;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.Consumers;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.Memberships;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Tests.Consumers;

public class MembershipEndedV1ConsumerTests
{
    [Fact]
    public async Task An_ended_membership_is_stored_as_an_ending_once_the_unit_of_work_commits()
    {
        var copies = new InMemoryMembershipCopies();
        var ended = MembershipEvents.Ended(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());

        await new MembershipEndedV1Consumer(copies, copies).HandleAsync(ended, TestContext.Current.CancellationToken);

        var ending = copies.Endings.ShouldHaveSingleItem();
        (ending.Id, ending.PersonId, ending.PharmacyId).ShouldBe((ended.MembershipId, ended.PersonId, ended.PharmacyId));
        copies.Members.ShouldBeEmpty();
        copies.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task An_ending_delivered_twice_is_stored_once_and_nothing_more_is_committed()
    {
        var copies = new InMemoryMembershipCopies();
        var consumer = new MembershipEndedV1Consumer(copies, copies);
        var ended = MembershipEvents.Ended(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());

        await consumer.HandleAsync(ended, TestContext.Current.CancellationToken);
        await consumer.HandleAsync(MembershipEvents.Ended(ended.MembershipId, ended.PersonId, ended.PharmacyId), TestContext.Current.CancellationToken);

        copies.Endings.ShouldHaveSingleItem().Id.ShouldBe(ended.MembershipId);
        copies.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task Ending_a_membership_keeps_its_member()
    {
        var copies = new InMemoryMembershipCopies();
        var started = MembershipEvents.Started(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());
        await new MembershipStartedV1Consumer(copies, copies, Substitute.For<IPersonalMessagePurge>(), copies, NullLogger<MembershipStartedV1Consumer>.Instance).HandleAsync(started, TestContext.Current.CancellationToken);

        await new MembershipEndedV1Consumer(copies, copies).HandleAsync(MembershipEvents.Ended(started.MembershipId, started.PersonId, started.PharmacyId), TestContext.Current.CancellationToken);

        copies.Members.ShouldHaveSingleItem().Id.ShouldBe(started.MembershipId);
        copies.Endings.ShouldHaveSingleItem().Id.ShouldBe(started.MembershipId);
    }

    [Fact]
    public async Task An_ending_arriving_before_its_start_is_kept_and_the_later_start_is_stored_too()
    {
        var copies = new InMemoryMembershipCopies();
        var membershipId = Guid.CreateVersion7();
        var personId = Guid.CreateVersion7();
        var pharmacyId = Guid.CreateVersion7();

        await new MembershipEndedV1Consumer(copies, copies).HandleAsync(MembershipEvents.Ended(membershipId, personId, pharmacyId), TestContext.Current.CancellationToken);
        await new MembershipStartedV1Consumer(copies, copies, Substitute.For<IPersonalMessagePurge>(), copies, NullLogger<MembershipStartedV1Consumer>.Instance).HandleAsync(MembershipEvents.Started(membershipId, personId, pharmacyId), TestContext.Current.CancellationToken);

        copies.Endings.ShouldHaveSingleItem().Id.ShouldBe(membershipId);
        copies.Members.ShouldHaveSingleItem().Id.ShouldBe(membershipId);
        (await copies.IsEndedAsync(MembershipId.From(membershipId), TestContext.Current.CancellationToken)).ShouldBeTrue();
        copies.Commits.ShouldBe(2);
    }

    [Theory]
    [InlineData(nameof(MembershipEndedV1.MembershipId))]
    [InlineData(nameof(MembershipEndedV1.PersonId))]
    [InlineData(nameof(MembershipEndedV1.PharmacyId))]
    public async Task An_empty_id_is_refused_by_its_name_and_nothing_stored(string field)
    {
        var copies = new InMemoryMembershipCopies();
        var ended = MembershipEvents.Ended(
            field == nameof(MembershipEndedV1.MembershipId) ? Guid.Empty : Guid.CreateVersion7(),
            field == nameof(MembershipEndedV1.PersonId) ? Guid.Empty : Guid.CreateVersion7(),
            field == nameof(MembershipEndedV1.PharmacyId) ? Guid.Empty : Guid.CreateVersion7());

        var refused = await Should.ThrowAsync<GuardException>(() => new MembershipEndedV1Consumer(copies, copies).HandleAsync(ended, TestContext.Current.CancellationToken));

        refused.ParamName.ShouldBe(field);
        copies.Endings.ShouldBeEmpty();
        copies.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task No_message_is_refused_and_nothing_stored()
    {
        var copies = new InMemoryMembershipCopies();

        await Should.ThrowAsync<GuardException>(() => new MembershipEndedV1Consumer(copies, copies).HandleAsync(null!, TestContext.Current.CancellationToken));

        copies.Endings.ShouldBeEmpty();
        copies.Commits.ShouldBe(0);
    }
}
