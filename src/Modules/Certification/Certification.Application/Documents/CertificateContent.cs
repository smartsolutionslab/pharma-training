using System.Globalization;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Application.Documents;

/// <summary>
/// What a certificate's PDF prints, already formatted from the certificate's snapshot (#50), so the renderer only lays
/// it out: the dates as <c>dd.MM.yyyy</c>, the issue day as the German day, the score as <c>85 %</c>. It holds no
/// pharmacy, certificate id or person id.
/// </summary>
public sealed record CertificateContent(
    string HolderName,
    string CourseTitle,
    int CourseVersionNumber,
    string IssuedOn,
    string Score,
    string ValidUntil,
    Uri VerificationLink,
    DateTimeOffset IssuedAt)
{
    /// <summary>The sentence under the holder's name.</summary>
    public string Statement => $"hat die Schulung „{CourseTitle}“ (Version {CourseVersionNumber.ToString(CultureInfo.InvariantCulture)}) erfolgreich abgeschlossen.";
}
