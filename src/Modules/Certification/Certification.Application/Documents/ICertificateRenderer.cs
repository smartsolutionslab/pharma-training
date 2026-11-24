namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Application.Documents;

/// <summary>Lays out a certificate's PDF from what it prints; implemented in the module's Infrastructure project (#50).</summary>
public interface ICertificateRenderer
{
    /// <summary>The PDF's bytes: one A4 page, German, with the verification link as a QR code.</summary>
    byte[] Render(CertificateContent content);
}
