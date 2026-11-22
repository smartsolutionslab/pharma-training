using System.Net;
using System.Security.Cryptography;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.DTOs;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.Queries.GetMyPharmacy;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Tests.Pharmacies;

public class GetMyPharmacyTests
{
    private static readonly Timestamp Now = Timestamp.From(new DateTimeOffset(2027, 3, 1, 9, 0, 0, TimeSpan.Zero));
    private static readonly PersonId Owner = PersonId.From(Guid.CreateVersion7());
    private static readonly PersonId Anna = PersonId.From(Guid.CreateVersion7());
    private static readonly EmailAddress AnnaMail = EmailAddress.From("anna@example.org");

    [Fact]
    public async Task A_member_reads_the_pharmacy_with_their_own_membership()
    {
        var store = new InMemoryPharmacyStore();
        var pharmacy = Register();
        store.Stored.Add(pharmacy);

        var mine = await new GetMyPharmacyHandler(store).HandleAsync(new GetMyPharmacy(pharmacy.Id.Value, Owner), TestContext.Current.CancellationToken);

        mine.Value.ShouldBe(new MyPharmacyDto(
            pharmacy.Id.Value,
            "Stadt-Apotheke",
            pharmacy.Memberships.Single().Id.Value,
            OrganisationMembershipRole.Manager,
            OrganisationJobFunction.Pharmacist,
            CanManageMembers: true,
            CanAssignCourses: true));
    }

    [Fact]
    public async Task A_pharmacy_I_am_not_an_active_member_of_is_not_found()
    {
        var store = new InMemoryPharmacyStore();
        var pharmacy = Register();
        pharmacy.Invite(MembershipId.New(), AnnaMail, JobFunction.Pta, MembershipRole.Employee, Now)
            .IsSuccess.ShouldBeTrue();
        store.Stored.Add(pharmacy);
        var handler = new GetMyPharmacyHandler(store);

        var invited = await handler.HandleAsync(new GetMyPharmacy(pharmacy.Id.Value, Anna), TestContext.Current.CancellationToken);
        var stranger = await handler.HandleAsync(new GetMyPharmacy(pharmacy.Id.Value, PersonId.From(Guid.CreateVersion7())), TestContext.Current.CancellationToken);

        invited.Error.Code.ShouldBe("organisation.pharmacy-not-found");
        invited.Error.Status.ShouldBe(HttpStatusCode.NotFound);
        stranger.Error.Code.ShouldBe("organisation.pharmacy-not-found");
        stranger.Error.Status.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task An_unknown_id_and_the_empty_guid_are_not_found()
    {
        var handler = new GetMyPharmacyHandler(new InMemoryPharmacyStore());

        var unknown = await handler.HandleAsync(new GetMyPharmacy(Guid.CreateVersion7(), Owner), TestContext.Current.CancellationToken);
        var empty = await handler.HandleAsync(new GetMyPharmacy(Guid.Empty, Owner), TestContext.Current.CancellationToken);

        unknown.Error.Code.ShouldBe("organisation.pharmacy-not-found");
        unknown.Error.Status.ShouldBe(HttpStatusCode.NotFound);
        empty.Error.Code.ShouldBe("organisation.pharmacy-not-found");
        empty.Error.Status.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public void What_a_caller_gets_wrong_when_registering_is_refused_with_its_own_code_and_status()
    {
        PharmacyFailures.InvalidName().Code.ShouldBe("organisation.invalid-pharmacy-name");
        PharmacyFailures.InvalidName().Status.ShouldBe(HttpStatusCode.BadRequest);
        PharmacyFailures.InvalidJobFunction().Code.ShouldBe("organisation.invalid-job-function");
        PharmacyFailures.InvalidJobFunction().Status.ShouldBe(HttpStatusCode.BadRequest);
        PharmacyFailures.IdentityIncomplete().Code.ShouldBe("organisation.identity-incomplete");
        PharmacyFailures.IdentityIncomplete().Status.ShouldBe(HttpStatusCode.Forbidden);
        PharmacyFailures.NotFound(Guid.CreateVersion7()).Status.ShouldBe(HttpStatusCode.NotFound);
    }

    private static Pharmacy Register() =>
        Pharmacy.Register(
            PharmacyId.New(),
            PharmacyName.From("Stadt-Apotheke"),
            MembershipId.New(),
            Owner,
            DisplayName.From("Olaf Owner"),
            EmailAddress.From("owner@example.com"),
            JobFunction.Pharmacist,
            Now);
}
