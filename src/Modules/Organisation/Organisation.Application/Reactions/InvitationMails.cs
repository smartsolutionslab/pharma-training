using System.Globalization;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Mail;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.Reactions;

/// <summary>
/// The invitation mail, in German plain text. The expiry is written in UTC with a fixed pattern and the invariant culture,
/// so neither the server's culture nor its time zone database changes it.
/// </summary>
public static class InvitationMails
{
    public static OutgoingMail Compose(PharmacyName name, EmailAddress email, Timestamp expiresAt, Uri link)
    {
        Ensure.That(name).IsNotNull();
        Ensure.That(email).IsNotNull();
        Ensure.That(expiresAt).IsNotNull();
        Ensure.That(link).IsNotNull();

        var until = expiresAt.Value.UtcDateTime.ToString("dd.MM.yyyy, HH:mm", CultureInfo.InvariantCulture);
        string[] lines =
        [
            "Guten Tag,",
            string.Empty,
            $"Sie wurden eingeladen, bei PharmaTraining der Apotheke „{name.Value}“ beizutreten.",
            "Öffnen Sie diesen Link, um die Einladung anzunehmen:",
            string.Empty,
            link.AbsoluteUri,
            string.Empty,
            $"Der Link ist gültig bis {until} Uhr (UTC). Wird die Einladung erneut gesendet, gilt nur noch der Link aus der neuesten E-Mail.",
            string.Empty,
            "Wenn Sie diese Einladung nicht erwartet haben, können Sie diese E-Mail ignorieren.",
        ];
        return new OutgoingMail(email.Value, $"Einladung zu {name.Value} bei PharmaTraining", string.Join("\n", lines));
    }
}
