using Net.Codecrete.QrCodeGenerator;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Infrastructure.Documents;

/// <summary>
/// The verification link as a QR code: medium error correction, which survives a crease or a smudge on paper, and the
/// quiet zone of four modules the standard asks for around it (#50).
/// </summary>
public static class CertificateQrCode
{
    public const int QuietZoneModules = 4;

    public static QrCode For(Uri link)
    {
        Ensure.That(link).IsNotNull();
        return QrCode.EncodeText(link.AbsoluteUri, QrCode.Ecc.Medium);
    }
}
