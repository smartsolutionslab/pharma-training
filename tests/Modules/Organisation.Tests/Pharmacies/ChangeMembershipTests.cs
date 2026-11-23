using System.Net;
using SmartSolutionsLab.PharmaTraining.Contracts.Organisation;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.Commands.ChangeMembership;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies.Events;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Tests.Pharmacies;

/// <summary>A manager changes a member's job function and role; other modules are told with the next version, and there is always a manager left.</summary>
public class ChangeMembershipTests
{
    private static readonly PersonId Owner = PersonId.From(Guid.CreateVersion7());
    private static readonly PersonId Anna = PersonId.From(Guid.CreateVersion7());
    private static readonly PersonId Ben = PersonId.From(Guid.CreateVersion7());

    [Fact]
    public async Task A_manager_changes_an_employees_job_function_and_announces_it_with_the_next_version()
    {
        var store = new InMemoryPharmacyStore();
        var pharmacy = TestPharmacies.Register(Owner);
        var anna = TestPharmacies.AddMember(pharmacy, Anna, MembershipRole.Employee, JobFunction.Pta);
        pharmacy.Stored(store);

        var changed = await Handler(store).HandleAsync(
            new ChangeMembership(pharmacy.Id, anna, Owner, JobFunction.Pka, MembershipRole.Employee),
            TestContext.Current.CancellationToken);

        changed.IsSuccess.ShouldBeTrue();
        changed.Value.ShouldBe(Unit.Value);
        pharmacy.Membership(anna).JobFunction.ShouldBe(JobFunction.Pka);
        pharmacy.Membership(anna).Role.ShouldBe(MembershipRole.Employee);
        pharmacy.DomainEvents.OfType<MembershipChanged>().ShouldHaveSingleItem()
            .ShouldBe(new MembershipChanged(pharmacy.Id, anna, Anna, DisplayName.From("Anna Berger"), JobFunction.Pka, MembershipRole.Employee, MembershipVersion.From(2)));
        var announced = store.Published.ShouldHaveSingleItem().ShouldBeOfType<MembershipChangedV1>();
        announced.MembershipId.ShouldBe(anna.Value);
        announced.PersonId.ShouldBe(Anna.Value);
        announced.PharmacyId.ShouldBe(pharmacy.Id.Value);
        announced.DisplayName.ShouldBe("Anna Berger");
        announced.JobFunction.ShouldBe(JobFunctionV1.Pka);
        announced.Role.ShouldBe(MembershipRoleV1.Employee);
        announced.Version.ShouldBe(2);
        store.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task A_change_that_changes_nothing_announces_nothing()
    {
        var store = new InMemoryPharmacyStore();
        var pharmacy = TestPharmacies.Register(Owner);
        var anna = TestPharmacies.AddMember(pharmacy, Anna, MembershipRole.Employee, JobFunction.Pta);
        pharmacy.Stored(store);

        var same = await Handler(store).HandleAsync(
            new ChangeMembership(pharmacy.Id, anna, Owner, JobFunction.Pta, MembershipRole.Employee),
            TestContext.Current.CancellationToken);

        same.IsSuccess.ShouldBeTrue();
        pharmacy.Membership(anna).Version.ShouldBe(MembershipVersion.First);
        store.Published.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_refused_change_announces_nothing()
    {
        var store = new InMemoryPharmacyStore();
        var pharmacy = TestPharmacies.Register(Owner).Stored(store);
        var founder = TestPharmacies.FounderOf(pharmacy);

        var refused = await Handler(store).HandleAsync(
            new ChangeMembership(pharmacy.Id, founder, Owner, JobFunction.Pharmacist, MembershipRole.Employee),
            TestContext.Current.CancellationToken);

        refused.IsSuccess.ShouldBeFalse();
        refused.Error.Code.ShouldBe("organisation.last-manager");
        pharmacy.Membership(founder).Version.ShouldBe(MembershipVersion.First);
        store.Published.ShouldBeEmpty();
        store.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task A_change_that_lost_the_race_announces_nothing()
    {
        var store = new InMemoryPharmacyStore();
        var pharmacy = TestPharmacies.Register(Owner);
        var anna = TestPharmacies.AddMember(pharmacy, Anna, MembershipRole.Employee, JobFunction.Pta);
        pharmacy.Stored(store);
        store.LoseNextCommit();

        var lost = await Handler(store).HandleAsync(
            new ChangeMembership(pharmacy.Id, anna, Owner, JobFunction.Pka, MembershipRole.Manager),
            TestContext.Current.CancellationToken);

        lost.IsSuccess.ShouldBeFalse();
        lost.Error.Code.ShouldBe("organisation.pharmacy-changed");
        lost.Error.Status.ShouldBe(HttpStatusCode.Conflict);
        store.Published.ShouldBeEmpty();
        store.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task A_manager_promotes_an_employee_to_manager()
    {
        var store = new InMemoryPharmacyStore();
        var pharmacy = TestPharmacies.Register(Owner);
        var anna = TestPharmacies.AddMember(pharmacy, Anna, MembershipRole.Employee, JobFunction.Pta);
        pharmacy.Stored(store);

        var promoted = await Handler(store).HandleAsync(
            new ChangeMembership(pharmacy.Id, anna, Owner, JobFunction.Pta, MembershipRole.Manager),
            TestContext.Current.CancellationToken);

        promoted.IsSuccess.ShouldBeTrue();
        pharmacy.Membership(anna).Role.ShouldBe(MembershipRole.Manager);
        store.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task The_only_manager_cannot_step_down()
    {
        var store = new InMemoryPharmacyStore();
        var pharmacy = TestPharmacies.Register(Owner).Stored(store);
        var founder = TestPharmacies.FounderOf(pharmacy);

        var refused = await Handler(store).HandleAsync(
            new ChangeMembership(pharmacy.Id, founder, Owner, JobFunction.Pharmacist, MembershipRole.Employee),
            TestContext.Current.CancellationToken);

        refused.IsSuccess.ShouldBeFalse();
        refused.Error.Code.ShouldBe("organisation.last-manager");
        refused.Error.Status.ShouldBe(HttpStatusCode.Conflict);
        pharmacy.Membership(founder).Role.ShouldBe(MembershipRole.Manager);
        pharmacy.DomainEvents.OfType<MembershipChanged>().ShouldBeEmpty();
        store.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task A_manager_steps_down_once_there_is_a_second_manager()
    {
        var store = new InMemoryPharmacyStore();
        var pharmacy = TestPharmacies.Register(Owner);
        TestPharmacies.AddMember(pharmacy, Anna, MembershipRole.Manager);
        pharmacy.Stored(store);
        var founder = TestPharmacies.FounderOf(pharmacy);

        var stepped = await Handler(store).HandleAsync(
            new ChangeMembership(pharmacy.Id, founder, Owner, JobFunction.Pharmacist, MembershipRole.Employee),
            TestContext.Current.CancellationToken);

        stepped.IsSuccess.ShouldBeTrue();
        pharmacy.Membership(founder).Role.ShouldBe(MembershipRole.Employee);
        store.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task The_values_the_membership_already_has_succeed_and_change_nothing()
    {
        var store = new InMemoryPharmacyStore();
        var pharmacy = TestPharmacies.Register(Owner);
        var anna = TestPharmacies.AddMember(pharmacy, Anna, MembershipRole.Employee, JobFunction.Pta);
        pharmacy.Stored(store);
        var founder = TestPharmacies.FounderOf(pharmacy);
        var handler = Handler(store);

        var same = await handler.HandleAsync(
            new ChangeMembership(pharmacy.Id, anna, Owner, JobFunction.Pta, MembershipRole.Employee),
            TestContext.Current.CancellationToken);
        var stillManager = await handler.HandleAsync(
            new ChangeMembership(pharmacy.Id, founder, Owner, JobFunction.Pharmacist, MembershipRole.Manager),
            TestContext.Current.CancellationToken);

        same.IsSuccess.ShouldBeTrue();
        stillManager.IsSuccess.ShouldBeTrue();
        pharmacy.DomainEvents.OfType<MembershipChanged>().ShouldBeEmpty();
        store.Published.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_employee_may_not_change_a_membership()
    {
        var store = new InMemoryPharmacyStore();
        var pharmacy = TestPharmacies.Register(Owner);
        var anna = TestPharmacies.AddMember(pharmacy, Anna, MembershipRole.Employee);
        pharmacy.Stored(store);
        var handler = Handler(store);

        var own = await handler.HandleAsync(
            new ChangeMembership(pharmacy.Id, anna, Anna, JobFunction.Pta, MembershipRole.Manager),
            TestContext.Current.CancellationToken);
        var others = await handler.HandleAsync(
            new ChangeMembership(pharmacy.Id, TestPharmacies.FounderOf(pharmacy), Anna, JobFunction.Pka, MembershipRole.Manager),
            TestContext.Current.CancellationToken);

        own.Error.Code.ShouldBe("organisation.capability-missing");
        own.Error.Status.ShouldBe(HttpStatusCode.Forbidden);
        others.Error.Code.ShouldBe("organisation.capability-missing");
        others.Error.Status.ShouldBe(HttpStatusCode.Forbidden);
        pharmacy.Membership(anna).Role.ShouldBe(MembershipRole.Employee);
        store.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task A_stranger_and_an_unknown_pharmacy_are_not_found()
    {
        var store = new InMemoryPharmacyStore();
        var pharmacy = TestPharmacies.Register(Owner);
        var anna = TestPharmacies.AddMember(pharmacy, Anna, MembershipRole.Employee);
        pharmacy.Stored(store);
        var handler = Handler(store);

        var stranger = await handler.HandleAsync(
            new ChangeMembership(pharmacy.Id, anna, Ben, JobFunction.Pka, MembershipRole.Employee),
            TestContext.Current.CancellationToken);
        var unknown = await handler.HandleAsync(
            new ChangeMembership(PharmacyId.New(), anna, Owner, JobFunction.Pka, MembershipRole.Employee),
            TestContext.Current.CancellationToken);

        stranger.Error.Code.ShouldBe("organisation.pharmacy-not-found");
        stranger.Error.Status.ShouldBe(HttpStatusCode.NotFound);
        unknown.Error.Code.ShouldBe("organisation.pharmacy-not-found");
        unknown.Error.Status.ShouldBe(HttpStatusCode.NotFound);
        pharmacy.Membership(anna).JobFunction.ShouldBe(JobFunction.Pta);
        store.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task An_invited_or_unknown_membership_is_not_found()
    {
        var store = new InMemoryPharmacyStore();
        var pharmacy = TestPharmacies.Register(Owner);
        var invited = TestPharmacies.Invite(pharmacy, "invited@example.org");
        pharmacy.Stored(store);
        var handler = Handler(store);

        var refusedInvited = await handler.HandleAsync(
            new ChangeMembership(pharmacy.Id, invited, Owner, JobFunction.Pka, MembershipRole.Manager),
            TestContext.Current.CancellationToken);
        var refusedUnknown = await handler.HandleAsync(
            new ChangeMembership(pharmacy.Id, MembershipId.New(), Owner, JobFunction.Pka, MembershipRole.Manager),
            TestContext.Current.CancellationToken);

        refusedInvited.Error.Code.ShouldBe("organisation.no-active-membership");
        refusedInvited.Error.Status.ShouldBe(HttpStatusCode.NotFound);
        refusedUnknown.Error.Code.ShouldBe("organisation.no-active-membership");
        refusedUnknown.Error.Status.ShouldBe(HttpStatusCode.NotFound);
        pharmacy.Membership(invited).Role.ShouldBe(MembershipRole.Employee);
        store.Commits.ShouldBe(0);
    }

    private static ChangeMembershipHandler Handler(InMemoryPharmacyStore store) => new(store, store);
}
