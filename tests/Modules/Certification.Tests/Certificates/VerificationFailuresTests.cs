using System.Net;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Application;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Tests.Certificates;

/// <summary>
/// What a verifier can be refused (#51): one 404 for every code that finds no certificate, which names no code, and a
/// 429 of the module's own for an address over the verification limit.
/// </summary>
public class VerificationFailuresTests
{
    [Fact]
    public void A_code_that_finds_nothing_is_404_certification_certificate_not_verifiable()
    {
        var error = VerificationFailures.NotVerifiable();

        error.ShouldBeOfType<CertificateNotVerifiable>();
        error.Code.ShouldBe("certification.certificate-not-verifiable");
        error.Status.ShouldBe(HttpStatusCode.NotFound);
        error.Message.ShouldBe("No certificate matches this code.");
        error.ShouldBe(VerificationFailures.NotVerifiable());
    }

    [Fact]
    public void An_address_over_the_limit_is_429_certification_verification_limit_reached()
    {
        var error = VerificationFailures.LimitReached();

        error.ShouldBeOfType<VerificationLimitReached>();
        error.Code.ShouldBe("certification.verification-limit-reached");
        error.Status.ShouldBe(HttpStatusCode.TooManyRequests);
        error.Message.ShouldBe("Too many verifications from this address; try again later.");
    }
}
