using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.Consumers;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Tests.Erasures;

/// <summary>
/// The erasure's confirmation mail (#115) goes to the address alone and says what happened: the erasure is carried out
/// on request, the memberships are ended and the invitations withdrawn, retained certificates stay until their retention
/// ends, and support is the place to turn to. It promises nothing that is not true yet when it is sent: Keycloak and the
/// other modules still hold the name and address then, so it never says they are gone everywhere.
/// </summary>
public class ErasureConfirmationMailsTests
{
    private static readonly EmailAddress Address = EmailAddress.From("Erika.Mustermann@Example.org");

    [Fact]
    public void The_mail_goes_to_the_address_and_names_neither_the_person_nor_a_pharmacy()
    {
        var mail = ErasureConfirmationMails.Compose(Address);

        mail.To.ShouldBe("erika.mustermann@example.org");
        mail.Text.ShouldNotContain("erika", Case.Insensitive);
        mail.Text.ShouldNotContain("mustermann", Case.Insensitive);
        mail.Text.ShouldNotContain("Apotheke „", Case.Sensitive);
    }

    [Fact]
    public void The_mail_says_what_the_erasure_does()
    {
        var text = ErasureConfirmationMails.Compose(Address).Text;

        text.ShouldContain("Art. 17 DSGVO", Case.Sensitive);
        text.ShouldContain("Mitgliedschaften", Case.Sensitive);
        text.ShouldContain("beendet", Case.Sensitive);
        text.ShouldContain("Einladungen", Case.Sensitive);
        text.ShouldContain("zurückgezogen", Case.Sensitive);
        text.ShouldContain("Zertifikate", Case.Sensitive);
        text.ShouldContain("Aufbewahrungsfrist", Case.Sensitive);
        text.ShouldContain("Support", Case.Sensitive);
    }

    [Fact]
    public void The_mail_does_not_claim_the_data_is_gone_everywhere_already()
    {
        var text = ErasureConfirmationMails.Compose(Address).Text;

        text.ShouldNotContain("speichern wir weder", Case.Insensitive);
        text.ShouldNotMatch(@"(?is)haben wir .*Daten.* gelöscht");
    }
}
