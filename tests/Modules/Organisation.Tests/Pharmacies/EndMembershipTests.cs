using System.Net;
using Microsoft.Extensions.Time.Testing;
using SmartSolutionsLab.PharmaTraining.Contracts.Organisation;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.Commands.EndMembership;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Tests.Pharmacies;

/// <summary>A manager ends a membership; it is announced to other modules with the commit, and refused without one.</summary>
public class EndMembershipTests
{
    private static readonly DateTimeOffset Start = new(2027, 4, 1, 8, 0, 0, TimeSpan.Zero);
    private static readonly PersonId Owner = PersonId.From(Guid.CreateVersion7());
    private static readonly PersonId Anna = PersonId.From(Guid.CreateVersion7());
    private static readonly PersonId Ben = PersonId.From(Guid.CreateVersion7());

    [Fact]
    public async Task A_manager_ends_an_employees_membership_and_it_is_announced_with_the_commit()
    {
        var store = new InMemoryPharmacyStore();
        var pharmacy = TestPharmacies.Register(Owner);
        var anna = TestPharmacies.AddMember(pharmacy, Anna, MembershipRole.Employee);
        pharmacy.Stored(store);
        var time = new FakeTimeProvider(Start);
        time.Advance(TimeSpan.FromMinutes(5));

        var ended = await new EndMembershipHandler(store, store, time)
            .HandleAsync(new EndMembership(pharmacy.Id, anna, Owner), TestContext.Current.CancellationToken);

        ended.IsSuccess.ShouldBeTrue();
        ended.Value.ShouldBe(Unit.Value);
        var membership = pharmacy.Membership(anna);
        membership.Status.ShouldBe(MembershipStatus.Ended);
        membership.EndedAt.ShouldBe(Timestamp.From(Start.AddMinutes(5)));
        store.Commits.ShouldBe(1);
        store.Published.ShouldHaveSingleItem().ShouldBeOfType<MembershipEndedV1>()
            .ShouldBe(new MembershipEndedV1(anna.Value, Anna.Value, pharmacy.Id.Value, Start.AddMinutes(5)));
    }

    [Fact]
    public async Task The_only_manager_cannot_end_their_own_membership()
    {
        var store = new InMemoryPharmacyStore();
        var pharmacy = TestPharmacies.Register(Owner).Stored(store);
        var founder = TestPharmacies.FounderOf(pharmacy);

        var refused = await Handler(store).HandleAsync(new EndMembership(pharmacy.Id, founder, Owner), TestContext.Current.CancellationToken);

        refused.IsSuccess.ShouldBeFalse();
        refused.Error.Code.ShouldBe("organisation.last-manager");
        refused.Error.Status.ShouldBe(HttpStatusCode.Conflict);
        pharmacy.Membership(founder).Status.ShouldBe(MembershipStatus.Active);
        store.Commits.ShouldBe(0);
        store.Published.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_manager_ends_their_own_membership_once_there_is_a_second_manager()
    {
        var store = new InMemoryPharmacyStore();
        var pharmacy = TestPharmacies.Register(Owner);
        TestPharmacies.AddMember(pharmacy, Anna, MembershipRole.Manager);
        pharmacy.Stored(store);
        var founder = TestPharmacies.FounderOf(pharmacy);

        var ended = await Handler(store).HandleAsync(new EndMembership(pharmacy.Id, founder, Owner), TestContext.Current.CancellationToken);

        ended.IsSuccess.ShouldBeTrue();
        pharmacy.Membership(founder).Status.ShouldBe(MembershipStatus.Ended);
        store.Commits.ShouldBe(1);
        store.Published.ShouldHaveSingleItem().ShouldBeOfType<MembershipEndedV1>().PersonId.ShouldBe(Owner.Value);
    }

    [Fact]
    public async Task An_employee_may_end_neither_someone_elses_membership_nor_their_own()
    {
        var store = new InMemoryPharmacyStore();
        var pharmacy = TestPharmacies.Register(Owner);
        var anna = TestPharmacies.AddMember(pharmacy, Anna, MembershipRole.Employee);
        pharmacy.Stored(store);
        var founder = TestPharmacies.FounderOf(pharmacy);
        var handler = Handler(store);

        var others = await handler.HandleAsync(new EndMembership(pharmacy.Id, founder, Anna), TestContext.Current.CancellationToken);
        var own = await handler.HandleAsync(new EndMembership(pharmacy.Id, anna, Anna), TestContext.Current.CancellationToken);

        others.Error.Code.ShouldBe("organisation.capability-missing");
        others.Error.Status.ShouldBe(HttpStatusCode.Forbidden);
        own.Error.Code.ShouldBe("organisation.capability-missing");
        own.Error.Status.ShouldBe(HttpStatusCode.Forbidden);
        pharmacy.Membership(founder).Status.ShouldBe(MembershipStatus.Active);
        pharmacy.Membership(anna).Status.ShouldBe(MembershipStatus.Active);
        store.Commits.ShouldBe(0);
        store.Published.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_stranger_and_an_unknown_pharmacy_are_not_found()
    {
        var store = new InMemoryPharmacyStore();
        var pharmacy = TestPharmacies.Register(Owner);
        var anna = TestPharmacies.AddMember(pharmacy, Anna, MembershipRole.Employee);
        pharmacy.Stored(store);
        var handler = Handler(store);

        var stranger = await handler.HandleAsync(new EndMembership(pharmacy.Id, anna, Ben), TestContext.Current.CancellationToken);
        var unknown = await handler.HandleAsync(new EndMembership(PharmacyId.New(), anna, Owner), TestContext.Current.CancellationToken);

        stranger.Error.Code.ShouldBe("organisation.pharmacy-not-found");
        stranger.Error.Status.ShouldBe(HttpStatusCode.NotFound);
        unknown.Error.Code.ShouldBe("organisation.pharmacy-not-found");
        unknown.Error.Status.ShouldBe(HttpStatusCode.NotFound);
        pharmacy.Membership(anna).Status.ShouldBe(MembershipStatus.Active);
        store.Commits.ShouldBe(0);
        store.Published.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_membership_that_is_not_active_in_this_pharmacy_is_not_found()
    {
        var store = new InMemoryPharmacyStore();
        var pharmacy = TestPharmacies.Register(Owner);
        var invited = TestPharmacies.Invite(pharmacy, "invited@example.org");
        var ended = TestPharmacies.AddMember(pharmacy, Anna, MembershipRole.Employee);
        pharmacy.EndMembership(ended, TestPharmacies.RegisteredAt.PlusDays(1)).IsSuccess.ShouldBeTrue();
        pharmacy.Stored(store);
        var elsewhere = TestPharmacies.FounderOf(TestPharmacies.Register(Owner, "Markt-Apotheke").Stored(store));
        var handler = Handler(store);
        List<MembershipId> notActiveHere = [MembershipId.New(), invited, ended, elsewhere];

        foreach (var membership in notActiveHere)
        {
            var refused = await handler.HandleAsync(new EndMembership(pharmacy.Id, membership, Owner), TestContext.Current.CancellationToken);

            refused.Error.Code.ShouldBe("organisation.no-active-membership");
            refused.Error.Status.ShouldBe(HttpStatusCode.NotFound);
        }

        pharmacy.Membership(invited).Status.ShouldBe(MembershipStatus.Invited);
        store.Commits.ShouldBe(0);
        store.Published.ShouldBeEmpty();
    }

    private static EndMembershipHandler Handler(InMemoryPharmacyStore store) => new(store, store, new FakeTimeProvider(Start));
}
