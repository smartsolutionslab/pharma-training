using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.Consumers;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Tests.Erasures;

public class ErasureConfirmationMailsTests
{
    [Fact]
    public void The_mail_names_neither_the_person_nor_a_pharmacy()
    {
        var mail = ErasureConfirmationMails.Compose(EmailAddress.From("Erika.Mustermann@Example.org"));

        mail.To.ShouldBe("erika.mustermann@example.org");
        mail.Subject.ShouldBe("Ihre Daten bei PharmaTraining wurden gelöscht");
        mail.Text.ShouldNotBeNullOrWhiteSpace();
        mail.Text.ShouldNotContain("erika", Case.Insensitive);
        mail.Text.ShouldNotContain("mustermann", Case.Insensitive);
        mail.Text.ShouldNotContain("Apotheke „", Case.Sensitive);
    }
}
