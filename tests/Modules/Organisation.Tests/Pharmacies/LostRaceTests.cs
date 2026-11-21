using System.Net;
using Microsoft.Extensions.Time.Testing;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.Commands.ChangeMembership;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.Commands.EndMembership;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Tests.Pharmacies;

/// <summary>A change whose pharmacy another save changed since it was loaded is refused as a conflict, not a fault.</summary>
public class LostRaceTests
{
    private static readonly PersonId Owner = PersonId.From(Guid.CreateVersion7());
    private static readonly PersonId Anna = PersonId.From(Guid.CreateVersion7());

    private readonly InMemoryPharmacyStore store = new();
    private readonly Pharmacy pharmacy;
    private readonly MembershipId anna;

    public LostRaceTests()
    {
        pharmacy = TestPharmacies.Register(Owner);
        anna = TestPharmacies.AddMember(pharmacy, Anna, MembershipRole.Employee, JobFunction.Pta);
        pharmacy.Stored(store);
        store.LoseNextCommit();
    }

    [Fact]
    public async Task An_ended_membership_that_lost_the_race_is_told_the_pharmacy_changed_and_nothing_is_announced()
    {
        var ended = await new EndMembershipHandler(store, store, new FakeTimeProvider())
            .HandleAsync(new EndMembership(pharmacy.Id, anna, Owner), TestContext.Current.CancellationToken);

        ShouldBeChanged(ended);
        store.Commits.ShouldBe(0);
        store.Published.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_changed_membership_that_lost_the_race_is_told_the_pharmacy_changed()
    {
        var changed = await new ChangeMembershipHandler(store, store)
            .HandleAsync(new ChangeMembership(pharmacy.Id, anna, Owner, JobFunction.Pka, MembershipRole.Manager), TestContext.Current.CancellationToken);

        ShouldBeChanged(changed);
        store.Commits.ShouldBe(0);
        store.Published.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_unit_of_work_that_did_not_lose_a_race_commits_and_says_so()
    {
        var unitOfWork = new InMemoryPharmacyStore();
        IOrganisationUnitOfWork organisation = unitOfWork;

        (await organisation.TryCommitAsync(TestContext.Current.CancellationToken)).ShouldBeTrue();

        unitOfWork.Commits.ShouldBe(1);
    }

    private void ShouldBeChanged(Result<Unit, ApiError> result)
    {
        result.IsSuccess.ShouldBeFalse("the lost commit was answered as a success");
        var error = result.Error;
        error.ShouldBe(PharmacyFailures.Changed(pharmacy.Id.Value));
        error.Code.ShouldBe("organisation.pharmacy-changed");
        error.Status.ShouldBe(HttpStatusCode.Conflict);
    }
}
