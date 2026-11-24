using System.Globalization;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Certificates;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Application.Documents;

/// <summary>What a certificate's PDF prints, taken from the certificate's snapshot alone; pure, no I/O.</summary>
public static class CertificateContents
{
    private const string DateFormat = "dd.MM.yyyy";

    public static CertificateContent ToContent(this Certificate certificate, Uri verificationLink)
    {
        Ensure.That(certificate).IsNotNull();
        Ensure.That(verificationLink).IsNotNull();

        return new CertificateContent(
            certificate.HolderName.Value,
            certificate.CourseTitle.Value,
            certificate.CourseVersionNumber.Value,
            certificate.IssuedAt.DayInGermany().Value.ToString(DateFormat, CultureInfo.InvariantCulture),
            $"{certificate.Score.Value.ToString(CultureInfo.InvariantCulture)} %",
            certificate.ValidUntil.Value.ToString(DateFormat, CultureInfo.InvariantCulture),
            verificationLink,
            certificate.IssuedAt.Value);
    }
}
