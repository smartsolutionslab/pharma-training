using SmartSolutionsLab.PharmaTraining.Contracts.Organisation;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.Consumers;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Tests.Consumers;

public class MembershipStartedV1ConsumerTests
{
    [Fact]
    public async Task A_started_membership_is_stored_with_its_person_and_pharmacy_once_the_unit_of_work_commits()
    {
        var copies = new InMemoryMembershipCopies();
        var started = MembershipEvents.Started(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());

        await new MembershipStartedV1Consumer(copies, copies).HandleAsync(started, TestContext.Current.CancellationToken);

        var member = copies.Members.ShouldHaveSingleItem();
        (member.Id, member.PersonId, member.PharmacyId).ShouldBe((started.MembershipId, started.PersonId, started.PharmacyId));
        copies.Endings.ShouldBeEmpty();
        copies.Changes.ShouldBeEmpty();
        copies.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task A_membership_delivered_twice_is_stored_once_and_nothing_more_is_committed()
    {
        var copies = new InMemoryMembershipCopies();
        var consumer = new MembershipStartedV1Consumer(copies, copies);
        var started = MembershipEvents.Started(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());

        await consumer.HandleAsync(started, TestContext.Current.CancellationToken);
        await consumer.HandleAsync(MembershipEvents.Started(started.MembershipId, started.PersonId, started.PharmacyId), TestContext.Current.CancellationToken);

        copies.Members.ShouldHaveSingleItem().Id.ShouldBe(started.MembershipId);
        copies.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task One_person_in_two_pharmacies_is_stored_as_two_members()
    {
        var copies = new InMemoryMembershipCopies();
        var consumer = new MembershipStartedV1Consumer(copies, copies);
        var personId = Guid.CreateVersion7();

        await consumer.HandleAsync(MembershipEvents.Started(Guid.CreateVersion7(), personId, Guid.CreateVersion7()), TestContext.Current.CancellationToken);
        await consumer.HandleAsync(MembershipEvents.Started(Guid.CreateVersion7(), personId, Guid.CreateVersion7()), TestContext.Current.CancellationToken);

        copies.Members.Count.ShouldBe(2);
        copies.Members.ShouldAllBe(member => member.PersonId == personId);
        copies.Members.Select(member => member.PharmacyId).Distinct().Count().ShouldBe(2);
    }

    [Theory]
    [InlineData(nameof(MembershipStartedV1.MembershipId))]
    [InlineData(nameof(MembershipStartedV1.PersonId))]
    [InlineData(nameof(MembershipStartedV1.PharmacyId))]
    public async Task An_empty_id_is_refused_by_its_name_and_nothing_stored(string field)
    {
        var copies = new InMemoryMembershipCopies();
        var started = MembershipEvents.Started(
            field == nameof(MembershipStartedV1.MembershipId) ? Guid.Empty : Guid.CreateVersion7(),
            field == nameof(MembershipStartedV1.PersonId) ? Guid.Empty : Guid.CreateVersion7(),
            field == nameof(MembershipStartedV1.PharmacyId) ? Guid.Empty : Guid.CreateVersion7());

        var refused = await Should.ThrowAsync<GuardException>(() => new MembershipStartedV1Consumer(copies, copies).HandleAsync(started, TestContext.Current.CancellationToken));

        refused.ParamName.ShouldBe(field);
        copies.Members.ShouldBeEmpty();
        copies.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task No_message_is_refused_and_nothing_stored()
    {
        var copies = new InMemoryMembershipCopies();

        await Should.ThrowAsync<GuardException>(() => new MembershipStartedV1Consumer(copies, copies).HandleAsync(null!, TestContext.Current.CancellationToken));

        copies.Members.ShouldBeEmpty();
        copies.Commits.ShouldBe(0);
    }
}
