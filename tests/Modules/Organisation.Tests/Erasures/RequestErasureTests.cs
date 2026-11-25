using System.Net;
using Microsoft.Extensions.Time.Testing;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Contracts.Organisation;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.Commands.RequestErasure;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Erasures;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Tests.Pharmacies;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Tests.Erasures;

/// <summary>
/// Support requests a person's erasure (#115): their active memberships end and are announced, the open invitations to
/// their addresses are revoked, the erasure is logged and its confirmation mail requested, all in one commit; refused
/// while the person is a pharmacy's last active manager.
/// </summary>
public class RequestErasureTests
{
    private static readonly DateTimeOffset Start = new(2027, 5, 3, 10, 0, 0, TimeSpan.Zero);
    private static readonly PersonId Support = PersonId.From(Guid.CreateVersion7());
    private static readonly PersonId Owner = PersonId.From(Guid.CreateVersion7());

    private readonly InMemoryPharmacyStore store = new();
    private readonly FakeTimeProvider time = new(Start);

    [Fact]
    public async Task The_last_active_manager_is_refused_with_its_code_and_nothing_is_saved_or_published()
    {
        var person = PersonId.From(Guid.CreateVersion7());
        var managed = TestPharmacies.Register(person, "Einzige-Leitung-Apotheke").Stored(store);
        var founder = TestPharmacies.FounderOf(managed);

        var refused = await Handler().HandleAsync(new RequestErasure(person, Support), TestContext.Current.CancellationToken);

        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe("organisation.erasure-refused-last-manager");
        refused.Error.Status.ShouldBe(HttpStatusCode.Conflict);
        managed.Membership(founder).Status.ShouldBe(MembershipStatus.Active);
        store.Commits.ShouldBe(0);
        store.Published.ShouldBeEmpty();
        store.StoredErasures.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_last_active_manager_of_one_pharmacy_is_refused_though_an_employee_elsewhere()
    {
        var person = PersonId.From(Guid.CreateVersion7());
        var elsewhere = TestPharmacies.Register(Owner, "Markt-Apotheke");
        TestPharmacies.AddMember(elsewhere, person, MembershipRole.Employee);
        elsewhere.Stored(store);
        TestPharmacies.Register(person, "Einzige-Leitung-Apotheke").Stored(store);

        var refused = await Handler().HandleAsync(new RequestErasure(person, Support), TestContext.Current.CancellationToken);

        refused.Error.Code.ShouldBe("organisation.erasure-refused-last-manager");
        store.Commits.ShouldBe(0);
        store.Published.ShouldBeEmpty();
        store.StoredErasures.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_manager_with_a_second_manager_is_released()
    {
        var person = PersonId.From(Guid.CreateVersion7());
        var pharmacy = TestPharmacies.Register(Owner);
        var membership = TestPharmacies.AddMember(pharmacy, person, MembershipRole.Manager);
        pharmacy.Stored(store);

        var requested = await Handler().HandleAsync(new RequestErasure(person, Support), TestContext.Current.CancellationToken);

        requested.IsSuccess.ShouldBeTrue();
        pharmacy.Membership(membership).Status.ShouldBe(MembershipStatus.Ended);
        store.Published.OfType<MembershipEndedV1>().ShouldHaveSingleItem()
            .ShouldBe(new MembershipEndedV1(membership.Value, person.Value, pharmacy.Id.Value, Start));
    }

    [Fact]
    public async Task An_employee_is_released_and_each_ended_membership_is_announced()
    {
        var person = PersonId.From(Guid.CreateVersion7());
        var first = TestPharmacies.Register(Owner, "Stadt-Apotheke");
        var inFirst = TestPharmacies.AddMember(first, person, MembershipRole.Employee);
        first.Stored(store);
        var second = TestPharmacies.Register(Owner, "Markt-Apotheke");
        var inSecond = TestPharmacies.AddMember(second, person, MembershipRole.Employee);
        second.Stored(store);

        var requested = await Handler().HandleAsync(new RequestErasure(person, Support), TestContext.Current.CancellationToken);

        requested.IsSuccess.ShouldBeTrue();
        first.Membership(inFirst).Status.ShouldBe(MembershipStatus.Ended);
        second.Membership(inSecond).Status.ShouldBe(MembershipStatus.Ended);
        store.Published.OfType<MembershipEndedV1>().ShouldBe(
            [
                new MembershipEndedV1(inFirst.Value, person.Value, first.Id.Value, Start),
                new MembershipEndedV1(inSecond.Value, person.Value, second.Id.Value, Start),
            ],
            ignoreOrder: true);
        store.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task Open_invitations_to_the_persons_addresses_are_revoked_in_every_pharmacy()
    {
        var person = PersonId.From(Guid.CreateVersion7());
        var home = TestPharmacies.Register(Owner, "Stadt-Apotheke");
        var membership = TestPharmacies.AddMember(home, person, MembershipRole.Employee);
        var address = home.Membership(membership).Email;
        home.Stored(store);
        var second = TestPharmacies.Register(Owner, "Markt-Apotheke");
        var toPersonInSecond = TestPharmacies.Invite(second, address.Value);
        var toSomeoneElse = TestPharmacies.Invite(second, "someone-else@example.org");
        second.Stored(store);
        var third = TestPharmacies.Register(Owner, "Rosen-Apotheke");
        var toPersonInThird = TestPharmacies.Invite(third, address.Value);
        third.Stored(store);

        var requested = await Handler().HandleAsync(new RequestErasure(person, Support), TestContext.Current.CancellationToken);

        requested.IsSuccess.ShouldBeTrue();
        second.Membership(toPersonInSecond).Status.ShouldBe(MembershipStatus.Revoked);
        third.Membership(toPersonInThird).Status.ShouldBe(MembershipStatus.Revoked);
        second.Membership(toSomeoneElse).Status.ShouldBe(MembershipStatus.Invited);
        store.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task The_erasure_is_logged_with_the_staff_actor_and_the_mail_is_requested()
    {
        var person = PersonId.From(Guid.CreateVersion7());
        var pharmacy = TestPharmacies.Register(Owner);
        TestPharmacies.AddMember(pharmacy, person, MembershipRole.Employee);
        pharmacy.Stored(store);

        var requested = await Handler().HandleAsync(new RequestErasure(person, Support), TestContext.Current.CancellationToken);

        requested.IsSuccess.ShouldBeTrue();
        var erasure = store.StoredErasures.ShouldHaveSingleItem();
        erasure.Id.Value.ShouldBe(requested.Value);
        erasure.RequestedBy.ShouldBe(Support);
        erasure.RequestedAt.ShouldBe(Timestamp.From(Start));
        erasure.Confirmation.ShouldBe(ErasureConfirmation.Pending);
        erasure.ConfirmationHandledAt.ShouldBeNull();
        erasure.Steps.ShouldBeEmpty();
        store.Published.OfType<ErasureConfirmationMailRequestedV1>().ShouldHaveSingleItem()
            .ShouldBe(new ErasureConfirmationMailRequestedV1(requested.Value, person.Value));
        store.Published.OfType<PersonErasureRequestedV1>().ShouldBeEmpty("the erasure is announced once its confirmation is mailed");
        store.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task Names_and_addresses_stay_until_the_confirmation_is_mailed()
    {
        var person = PersonId.From(Guid.CreateVersion7());
        var pharmacy = TestPharmacies.Register(Owner);
        var membership = TestPharmacies.AddMember(pharmacy, person, MembershipRole.Employee, name: "Erika Erased");
        var address = pharmacy.Membership(membership).Email;
        pharmacy.Stored(store);

        (await Handler().HandleAsync(new RequestErasure(person, Support), TestContext.Current.CancellationToken)).IsSuccess.ShouldBeTrue();

        pharmacy.Membership(membership).Name.ShouldBe(DisplayName.From("Erika Erased"));
        pharmacy.Membership(membership).Email.ShouldBe(address);
    }

    [Fact]
    public async Task An_unknown_person_is_erased_with_nothing_to_release()
    {
        var stranger = PersonId.From(Guid.CreateVersion7());
        var pharmacy = TestPharmacies.Register(Owner);
        var member = TestPharmacies.AddMember(pharmacy, PersonId.From(Guid.CreateVersion7()), MembershipRole.Employee);
        pharmacy.Stored(store);

        var requested = await Handler().HandleAsync(new RequestErasure(stranger, Support), TestContext.Current.CancellationToken);

        requested.IsSuccess.ShouldBeTrue();
        pharmacy.Membership(member).Status.ShouldBe(MembershipStatus.Active);
        store.Published.ShouldHaveSingleItem().ShouldBe(new ErasureConfirmationMailRequestedV1(requested.Value, stranger.Value));
        store.StoredErasures.ShouldHaveSingleItem().Id.Value.ShouldBe(requested.Value);
        store.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task A_lost_race_answers_concurrent_save()
    {
        var person = PersonId.From(Guid.CreateVersion7());
        var pharmacy = TestPharmacies.Register(Owner);
        TestPharmacies.AddMember(pharmacy, person, MembershipRole.Employee);
        pharmacy.Stored(store);
        store.LoseNextCommit(new VersionMovedOn());

        var lost = await Handler().HandleAsync(new RequestErasure(person, Support), TestContext.Current.CancellationToken);

        lost.IsFailure.ShouldBeTrue();
        lost.Error.Code.ShouldBe("organisation.concurrent-save");
        lost.Error.Status.ShouldBe(HttpStatusCode.Conflict);
        store.Published.ShouldBeEmpty();
        store.StoredErasures.ShouldBeEmpty();
    }

    private RequestErasureHandler Handler() => new(store, store, store, time);
}
