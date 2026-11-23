using System.Buffers.Text;
using System.Security.Cryptography;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Certificates;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Application;

/// <summary>Creates the code of a certificate's QR link: 192 random bits, 32 URL-safe characters.</summary>
public static class VerificationCodes
{
    private const int Bytes = 24;

    public static VerificationCode Next() => VerificationCode.From(Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(Bytes)));
}
