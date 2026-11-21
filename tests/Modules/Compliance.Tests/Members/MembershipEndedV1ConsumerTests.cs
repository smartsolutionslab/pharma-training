using SmartSolutionsLab.PharmaTraining.Contracts.Organisation;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.Consumers;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Members;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Tests.Members;

public class MembershipEndedV1ConsumerTests
{
    [Fact]
    public async Task An_ended_membership_is_stored_as_an_ending_once_the_unit_of_work_commits()
    {
        var store = new InMemoryMemberStore();
        var membershipId = Guid.CreateVersion7();
        var personId = Guid.CreateVersion7();
        var pharmacyId = Guid.CreateVersion7();

        await new MembershipEndedV1Consumer(store, store).HandleAsync(MembershipEvents.Ended(membershipId, personId, pharmacyId), TestContext.Current.CancellationToken);

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
        var consumer = new MembershipEndedV1Consumer(store, store);
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
        await new MembershipStartedV1Consumer(store, store).HandleAsync(started, TestContext.Current.CancellationToken);

        await new MembershipEndedV1Consumer(store, store).HandleAsync(MembershipEvents.Ended(started.MembershipId, started.PersonId, started.PharmacyId), TestContext.Current.CancellationToken);

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

        await new MembershipEndedV1Consumer(store, store).HandleAsync(MembershipEvents.Ended(membershipId, personId, pharmacyId), TestContext.Current.CancellationToken);
        await new MembershipStartedV1Consumer(store, store).HandleAsync(MembershipEvents.Started(membershipId, personId, pharmacyId, "Ada"), TestContext.Current.CancellationToken);

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

        var refused = await Should.ThrowAsync<GuardException>(() => new MembershipEndedV1Consumer(store, store).HandleAsync(ended, TestContext.Current.CancellationToken));

        refused.ParamName.ShouldBe(field);
        store.Endings.ShouldBeEmpty();
        store.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task No_message_is_refused_and_nothing_stored()
    {
        var store = new InMemoryMemberStore();

        await Should.ThrowAsync<GuardException>(() => new MembershipEndedV1Consumer(store, store).HandleAsync(null!, TestContext.Current.CancellationToken));

        store.Endings.ShouldBeEmpty();
        store.Commits.ShouldBe(0);
    }
}
