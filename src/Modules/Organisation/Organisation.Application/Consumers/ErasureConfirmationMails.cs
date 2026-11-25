using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Mail;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.Consumers;

/// <summary>The erasure's confirmation mail, in German plain text; it names neither the person nor a pharmacy.</summary>
public static class ErasureConfirmationMails
{
    public const string Subject = "Ihre Daten bei PharmaTraining wurden gelöscht";

    public static OutgoingMail Compose(EmailAddress address)
    {
        Ensure.That(address).IsNotNull();

        string[] lines =
        [
            "Guten Tag,",
            string.Empty,
            "auf Ihren Wunsch haben wir Ihre personenbezogenen Daten bei PharmaTraining gelöscht (Art. 17 DSGVO).",
            "Ihre Mitgliedschaften in Apotheken sind beendet, offene Einladungen an diese Adresse wurden zurückgezogen.",
            "Nach dieser E-Mail speichern wir weder Ihren Namen noch diese Adresse.",
            string.Empty,
            "Zertifikate, die eine Apotheke für ihre gesetzlichen Nachweispflichten aufbewahren muss, bleiben bis zum Ende der Aufbewahrungsfrist erhalten.",
            string.Empty,
            "Wenn Sie diese Löschung nicht beauftragt haben, wenden Sie sich bitte an den Support von PharmaTraining.",
        ];
        return new OutgoingMail(address.Value, Subject, string.Join("\n", lines));
    }
}
