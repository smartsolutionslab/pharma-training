using System.Net;
using Microsoft.Extensions.Time.Testing;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.Commands.EndMembership;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Tests.Pharmacies;

/// <summary>
/// A save PostgreSQL ended as a deadlock or a serialization failure is not a pharmacy that changed: what the caller
/// loaded may still be current and the save rolled back, so it is told to try again (organisation.concurrent-save), not
/// to load the pharmacy again. A row version that moved on is still organisation.pharmacy-changed.
/// </summary>
public class DeadlockedSaveTests
{
    private static readonly PersonId Owner = PersonId.From(Guid.CreateVersion7());
    private static readonly PersonId Anna = PersonId.From(Guid.CreateVersion7());

    [Theory]
    [InlineData("40P01")]
    [InlineData("40001")]
    public void A_deadlocked_save_is_a_concurrent_save_to_try_again(string sqlState)
    {
        var error = PharmacyFailures.LostRace(new SaveDeadlocked(sqlState), Guid.CreateVersion7());

        error.Code.ShouldBe("organisation.concurrent-save");
        error.Status.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public void A_version_that_moved_on_is_still_a_pharmacy_that_changed()
    {
        var error = PharmacyFailures.LostRace(new VersionMovedOn(), Guid.CreateVersion7());

        error.Code.ShouldBe("organisation.pharmacy-changed");
        error.Status.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public void An_acceptance_whose_save_deadlocked_is_a_concurrent_save_and_one_whose_invitation_moved_on_is_an_invitation_that_changed()
    {
        PharmacyFailures.InvitationLostRace(new SaveDeadlocked("40P01")).Code.ShouldBe("organisation.concurrent-save");
        PharmacyFailures.InvitationLostRace(new VersionMovedOn()).Code.ShouldBe("organisation.invitation-changed");
    }

    [Fact]
    public async Task An_ended_membership_whose_save_deadlocked_is_told_to_try_again_and_nothing_is_saved()
    {
        var store = new InMemoryPharmacyStore();
        var pharmacy = TestPharmacies.Register(Owner);
        var anna = TestPharmacies.AddMember(pharmacy, Anna, MembershipRole.Employee, JobFunction.Pta);
        pharmacy.Stored(store);
        store.LoseNextCommit(new SaveDeadlocked("40P01"));

        var ended = await new EndMembershipHandler(store, store, new FakeTimeProvider())
            .HandleAsync(new EndMembership(pharmacy.Id, anna, Owner), TestContext.Current.CancellationToken);

        ended.IsFailure.ShouldBeTrue();
        ended.Error.Code.ShouldBe("organisation.concurrent-save");
        store.Commits.ShouldBe(0);
    }
}
