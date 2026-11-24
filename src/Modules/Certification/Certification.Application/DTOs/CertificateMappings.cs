using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Certificates;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Application.DTOs;

public static class CertificateMappings
{
    /// <summary>What a verifier sees of the certificate on <paramref name="today"/>, a German day.</summary>
    public static CertificateVerificationDto ToVerificationDto(this Certificate certificate, CalendarDate today)
    {
        Ensure.That(certificate).IsNotNull();
        Ensure.That(today).IsNotNull();

        return new CertificateVerificationDto(
            certificate.HolderName.Value,
            certificate.CourseTitle.Value,
            certificate.IssuedAt.DayInGermany().Value,
            certificate.ValidUntil.Value,
            certificate.ValidityOn(today).ToDto());
    }

    /// <summary>What the holder sees of their certificate on <paramref name="today"/>, a German day.</summary>
    public static HeldCertificateDto ToHeldCertificateDto(this Certificate certificate, CalendarDate today)
    {
        Ensure.That(certificate).IsNotNull();
        Ensure.That(today).IsNotNull();

        return new HeldCertificateDto(
            certificate.Id.Value,
            certificate.CourseTitle.Value,
            certificate.IssuedAt.DayInGermany().Value,
            certificate.ValidUntil.Value,
            certificate.ValidityOn(today).ToDto(),
            certificate.Document == DocumentState.Stored);
    }
}
