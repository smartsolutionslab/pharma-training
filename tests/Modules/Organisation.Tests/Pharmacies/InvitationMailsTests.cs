using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Mail;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.Consumers;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Tests.Pharmacies;

/// <summary>The invitation mail is German plain text with the link, how long it works, and that only the newest link does.</summary>
public class InvitationMailsTests
{
    private static readonly Timestamp Now = Timestamp.From(new DateTimeOffset(2026, 11, 21, 10, 0, 0, TimeSpan.Zero));
    private static readonly Uri Link = new("https://app.example/invitations/accept#token=0123456789abcdef0123456789abcdef0123456789a");

    [Fact]
    public void The_mail_goes_to_the_invited_address_and_names_the_pharmacy_in_its_subject()
    {
        var mail = Compose();

        mail.To.ShouldBe("anna@example.org");
        mail.Subject.ShouldBe("Einladung zu Apotheke am Markt bei PharmaTraining");
    }

    [Fact]
    public void The_text_holds_the_link_on_a_line_of_its_own()
    {
        var lines = Compose().Text.ReplaceLineEndings("\n").Split('\n');

        lines.ShouldContain(Link.AbsoluteUri);
    }

    [Fact]
    public void The_text_says_until_when_the_link_works_in_utc_and_that_only_the_newest_link_does()
    {
        var text = Compose().Text;

        text.ShouldContain("Apotheke am Markt", Case.Sensitive);
        text.ShouldContain("gültig bis 05.12.2026, 10:00 Uhr (UTC)", Case.Sensitive);
        text.ShouldContain("Wird die Einladung erneut gesendet, gilt nur noch der Link aus der neuesten E-Mail.", Case.Sensitive);
    }

    /// <summary>The mail of an invitation of anna@example.org to "Apotheke am Markt" made at <see cref="Now"/>.</summary>
    private static OutgoingMail Compose() =>
        InvitationMails.Compose(PharmacyName.From("Apotheke am Markt"), EmailAddress.From("anna@example.org"), Now.PlusDays(Pharmacy.InvitationValidityDays), Link);
}
