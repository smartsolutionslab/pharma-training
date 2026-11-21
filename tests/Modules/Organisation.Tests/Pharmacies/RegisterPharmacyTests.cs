using Microsoft.Extensions.Time.Testing;
using SmartSolutionsLab.PharmaTraining.Contracts.Organisation;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.Commands.RegisterPharmacy;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Tests.Pharmacies;

public class RegisterPharmacyTests
{
    private static readonly DateTimeOffset Start = new(2027, 3, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly PersonId Owner = PersonId.From(Guid.CreateVersion7());

    [Fact]
    public async Task A_registered_pharmacy_is_stored_once_the_unit_of_work_commits_and_its_id_answered()
    {
        var store = new InMemoryPharmacyStore();

        var registered = await Handler(store).HandleAsync(Command("Stadt-Apotheke"), TestContext.Current.CancellationToken);

        var pharmacy = store.Stored.ShouldHaveSingleItem();
        registered.Value.ShouldBe(pharmacy.Id.Value);
        registered.Value.Version.ShouldBe(7);
        pharmacy.Name.ShouldBe(PharmacyName.From("Stadt-Apotheke"));
        pharmacy.RegisteredAt.ShouldBe(Timestamp.From(Start));
        store.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task The_founder_is_the_active_manager_with_the_job_function_name_address_and_person_given()
    {
        var store = new InMemoryPharmacyStore();

        await Handler(store).HandleAsync(Command("Stadt-Apotheke", JobFunction.Pta), TestContext.Current.CancellationToken);

        var founder = store.Stored.ShouldHaveSingleItem().Memberships.ShouldHaveSingleItem();
        founder.Status.ShouldBe(MembershipStatus.Active);
        founder.Role.ShouldBe(MembershipRole.Manager);
        founder.JobFunction.ShouldBe(JobFunction.Pta);
        founder.Person.ShouldBe(Owner);
        founder.Name.ShouldBe(DisplayName.From("Olaf Owner"));
        founder.Email.ShouldBe(EmailAddress.From("owner@example.com"));
        founder.StartedAt.ShouldBe(Timestamp.From(Start));
    }

    [Fact]
    public async Task Registering_publishes_that_the_pharmacy_was_registered_and_that_the_founders_membership_started_with_the_commit()
    {
        var store = new InMemoryPharmacyStore();
        var time = new FakeTimeProvider(Start);
        time.Advance(TimeSpan.FromMinutes(5));

        var registered = await new RegisterPharmacyHandler(store, store, time)
            .HandleAsync(Command("Stadt-Apotheke", JobFunction.Pharmacist), TestContext.Current.CancellationToken);

        var pharmacy = store.Stored.ShouldHaveSingleItem();
        var founder = pharmacy.Memberships.ShouldHaveSingleItem();
        store.Published.Count.ShouldBe(2);
        var pharmacyRegistered = store.Published.OfType<PharmacyRegisteredV1>().ShouldHaveSingleItem();
        pharmacyRegistered.PharmacyId.ShouldBe(registered.Value);
        pharmacyRegistered.Name.ShouldBe("Stadt-Apotheke");
        var membershipStarted = store.Published.OfType<MembershipStartedV1>().ShouldHaveSingleItem();
        membershipStarted.MembershipId.ShouldBe(founder.Id.Value);
        membershipStarted.PersonId.ShouldBe(Owner.Value);
        membershipStarted.PharmacyId.ShouldBe(registered.Value);
        membershipStarted.DisplayName.ShouldBe("Olaf Owner");
        membershipStarted.JobFunction.ShouldBe(JobFunctionV1.Pharmacist);
        membershipStarted.Role.ShouldBe(MembershipRoleV1.Manager);
        membershipStarted.StartedAt.ShouldBe(Start.AddMinutes(5));
    }

    private static RegisterPharmacyHandler Handler(InMemoryPharmacyStore store) =>
        new(store, store, new FakeTimeProvider(Start));

    private static RegisterPharmacy Command(string name, JobFunction jobFunction = JobFunction.Pharmacist) =>
        new(PharmacyName.From(name), jobFunction, Owner, DisplayName.From("Olaf Owner"), EmailAddress.From("owner@example.com"));
}
