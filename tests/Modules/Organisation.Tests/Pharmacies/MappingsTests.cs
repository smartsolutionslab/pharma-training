using SmartSolutionsLab.PharmaTraining.Contracts.Organisation;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.DTOs;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.IntegrationEvents;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies.Events;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Tests.Pharmacies;

public class MappingsTests
{
    private static readonly Timestamp Now = Timestamp.From(new DateTimeOffset(2027, 3, 1, 9, 0, 0, TimeSpan.Zero));

    public static TheoryData<JobFunction> JobFunctions
    {
        get
        {
            var data = new TheoryData<JobFunction>();
            foreach (var jobFunction in Enum.GetValues<JobFunction>())
            {
                data.Add(jobFunction);
            }

            return data;
        }
    }

    public static TheoryData<MembershipRole> MembershipRoles
    {
        get
        {
            var data = new TheoryData<MembershipRole>();
            foreach (var role in Enum.GetValues<MembershipRole>())
            {
                data.Add(role);
            }

            return data;
        }
    }

    public static TheoryData<OrganisationMembershipRole> RequestedRoles
    {
        get
        {
            var data = new TheoryData<OrganisationMembershipRole>();
            foreach (var role in Enum.GetValues<OrganisationMembershipRole>())
            {
                data.Add(role);
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(JobFunctions))]
    public void A_job_function_travels_under_its_own_name_to_the_dto_and_back(JobFunction jobFunction)
    {
        var dto = jobFunction.ToDto();

        dto.ToString().ShouldBe(jobFunction.ToString());
        dto.ToDomain().ShouldBe(jobFunction);
    }

    [Theory]
    [MemberData(nameof(MembershipRoles))]
    public void A_membership_role_travels_under_its_own_name_to_the_dto(MembershipRole role) =>
        role.ToDto().ToString().ShouldBe(role.ToString());

    [Theory]
    [MemberData(nameof(RequestedRoles))]
    public void A_requested_membership_role_travels_under_its_own_name_to_the_domain(OrganisationMembershipRole role) =>
        role.ToDomain().ToString().ShouldBe(role.ToString());

    [Fact]
    public void An_undefined_requested_membership_role_is_a_bug() =>
        Should.Throw<System.Diagnostics.UnreachableException>(() => ((OrganisationMembershipRole)42).ToDomain());

    [Theory]
    [MemberData(nameof(JobFunctions))]
    public void A_job_function_travels_under_its_own_name_in_the_membership_started_event(JobFunction jobFunction) =>
        Started(jobFunction, MembershipRole.Employee).ToIntegrationEvent().JobFunction.ToString().ShouldBe(jobFunction.ToString());

    [Theory]
    [MemberData(nameof(MembershipRoles))]
    public void A_membership_role_travels_under_its_own_name_in_the_membership_started_event(MembershipRole role) =>
        Started(JobFunction.Pharmacist, role).ToIntegrationEvent().Role.ToString().ShouldBe(role.ToString());

    [Fact]
    public void An_undefined_job_function_is_a_bug()
    {
        Should.Throw<System.Diagnostics.UnreachableException>(() => ((JobFunction)99).ToDto());
        Should.Throw<System.Diagnostics.UnreachableException>(() => ((OrganisationJobFunction)99).ToDomain());
        Should.Throw<System.Diagnostics.UnreachableException>(() => ((MembershipRole)99).ToDto());
    }

    [Fact]
    public void A_registered_pharmacy_is_announced_with_its_id_and_name()
    {
        var id = PharmacyId.New();

        var announced = new PharmacyRegistered(id, PharmacyName.From("Stadt-Apotheke")).ToIntegrationEvent();

        announced.ShouldBe(new PharmacyRegisteredV1(id.Value, "Stadt-Apotheke"));
    }

    [Fact]
    public void A_started_membership_is_announced_with_every_field()
    {
        var started = Started(JobFunction.Pka, MembershipRole.Manager);

        var announced = started.ToIntegrationEvent();

        announced.ShouldBe(new MembershipStartedV1(
            started.Membership.Value,
            started.Person.Value,
            started.Pharmacy.Value,
            "Olaf Owner",
            JobFunctionV1.Pka,
            MembershipRoleV1.Manager,
            Now.Value));
    }

    [Fact]
    public void MembershipChanged_maps_to_MembershipChangedV1_field_by_field()
    {
        var changed = new MembershipChanged(
            PharmacyId.New(),
            MembershipId.New(),
            PersonId.From(Guid.CreateVersion7()),
            DisplayName.From("Anna Berger"),
            JobFunction.Pta,
            MembershipRole.Manager,
            MembershipVersion.From(3));

        var announced = changed.ToIntegrationEvent();

        announced.ShouldBe(new MembershipChangedV1(
            changed.Membership.Value,
            changed.Person.Value,
            changed.Pharmacy.Value,
            "Anna Berger",
            JobFunctionV1.Pta,
            MembershipRoleV1.Manager,
            3));
    }

    [Fact]
    public void An_ended_membership_is_announced_with_every_field()
    {
        var ended = new MembershipEnded(PharmacyId.New(), MembershipId.New(), PersonId.From(Guid.CreateVersion7()), Now);

        var announced = ended.ToIntegrationEvent();

        announced.ShouldBe(new MembershipEndedV1(ended.Membership.Value, ended.Person.Value, ended.Pharmacy.Value, Now.Value));
    }

    private static MembershipStarted Started(JobFunction jobFunction, MembershipRole role) =>
        new(PharmacyId.New(), MembershipId.New(), PersonId.From(Guid.CreateVersion7()), DisplayName.From("Olaf Owner"), jobFunction, role, Now);
}
