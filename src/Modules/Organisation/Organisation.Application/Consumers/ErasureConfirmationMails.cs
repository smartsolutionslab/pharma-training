using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Mail;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.Consumers;

/// <summary>The erasure's confirmation mail, in German plain text; it names neither the person nor a pharmacy.</summary>
public static class ErasureConfirmationMails
{
    public const string Subject = "Löschung Ihrer Daten bei PharmaTraining";

    public static OutgoingMail Compose(EmailAddress address)
    {
        Ensure.That(address).IsNotNull();

        string[] lines =
        [
            "Guten Tag,",
            string.Empty,
            "die Löschung Ihrer personenbezogenen Daten bei PharmaTraining nach Art. 17 DSGVO wurde beauftragt und läuft.",
            "Ihre Mitgliedschaften in Apotheken sind beendet, offene Einladungen an diese Adresse wurden zurückgezogen.",
            "Ihr Name und diese Adresse werden direkt nach dieser E-Mail aus den Mitgliedschaftsdaten entfernt und in den folgenden Schritten aus den übrigen Bereichen von PharmaTraining.",
            string.Empty,
            "Zertifikate, die eine Apotheke für ihre gesetzlichen Nachweispflichten aufbewahren muss, bleiben bis zum Ende ihrer Aufbewahrungsfrist erhalten.",
            string.Empty,
            "Bei Fragen oder wenn Sie diese Löschung nicht beauftragt haben, wenden Sie sich bitte an den Support von PharmaTraining.",
        ];
        return new OutgoingMail(address.Value, Subject, string.Join("\n", lines));
    }
}
