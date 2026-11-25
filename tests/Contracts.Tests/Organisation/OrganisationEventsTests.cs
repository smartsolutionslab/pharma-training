using SmartSolutionsLab.PharmaTraining.Contracts.Organisation;

namespace SmartSolutionsLab.PharmaTraining.Contracts.Tests.Organisation;

public class OrganisationEventsTests
{
    [Fact]
    public void PharmacyRegisteredV1_survives_the_wire()
    {
        Wire.ShouldSurviveTheWire(new PharmacyRegisteredV1(Guid.CreateVersion7(), "Löwen-Apotheke"));
    }

    [Fact]
    public void MembershipStartedV1_survives_the_wire()
    {
        var started = new MembershipStartedV1(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            "Anna Beispiel",
            JobFunctionV1.Pta,
            MembershipRoleV1.Manager,
            new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.FromHours(2)));

        var json = Wire.ShouldSurviveTheWire(started);

        json.ShouldContain("\"jobFunction\":\"Pta\"", Case.Sensitive);
        json.ShouldContain("\"role\":\"Manager\"", Case.Sensitive);
    }

    [Fact]
    public void MembershipChangedV1_survives_the_wire()
    {
        var changed = new MembershipChangedV1(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            "Anna Beispiel",
            JobFunctionV1.Pka,
            MembershipRoleV1.Manager,
            2);

        var json = Wire.ShouldSurviveTheWire(changed);

        json.ShouldContain("\"jobFunction\":\"Pka\"", Case.Sensitive);
        json.ShouldContain("\"role\":\"Manager\"", Case.Sensitive);
        json.ShouldContain("\"version\":2", Case.Sensitive);
    }

    [Fact]
    public void MembershipEndedV1_survives_the_wire()
    {
        Wire.ShouldSurviveTheWire(new MembershipEndedV1(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            new DateTimeOffset(2026, 11, 30, 17, 0, 0, TimeSpan.FromHours(1))));
    }

    [Fact]
    public void InvitationMailRequestedV1_survives_the_wire()
    {
        var json = Wire.ShouldSurviveTheWire(new InvitationMailRequestedV1(Guid.CreateVersion7(), Guid.CreateVersion7()));

        json.ShouldNotContain("token", Case.Insensitive);
        json.ShouldNotContain("email", Case.Insensitive);
    }

    /// <summary>#115 slice 6: the local request to delete an erased person's Keycloak account carries ids only.</summary>
    [Fact]
    public void AccountDeletionRequestedV1_survives_the_wire()
    {
        var json = Wire.ShouldSurviveTheWire(new AccountDeletionRequestedV1(Guid.CreateVersion7(), Guid.CreateVersion7()));

        json.ShouldNotContain("email", Case.Insensitive);
    }
}
