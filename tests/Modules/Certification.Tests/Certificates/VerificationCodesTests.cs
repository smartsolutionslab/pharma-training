using SmartSolutionsLab.PharmaTraining.Modules.Certification.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Certificates;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Tests.Certificates;

/// <summary>The code in a certificate's QR link: 192 random bits, 32 URL-safe characters, never the same twice.</summary>
public class VerificationCodesTests
{
    [Fact]
    public void A_verification_code_is_32_url_safe_characters()
    {
        var code = VerificationCodes.Next();

        code.Value.Length.ShouldBe(32);
        code.Value.ShouldAllBe(character => char.IsAsciiLetterOrDigit(character) || character == '-' || character == '_');
        code.ShouldBe(VerificationCode.From(code.Value));
    }

    [Fact]
    public void Each_verification_code_is_another()
    {
        HashSet<string> codes = [.. Enumerable.Range(0, 100).Select(_ => VerificationCodes.Next().Value)];

        codes.Count.ShouldBe(100);
    }
}
