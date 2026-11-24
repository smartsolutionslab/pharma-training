using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Application.DTOs;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Certificates;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Application.Queries.VerifyCertificate;

/// <summary>
/// Shows the certificate a code names, valid or expired on today's German day. A malformed code is refused without a
/// lookup, with the same refusal as a code that finds nothing, so no input tells more than that it found nothing.
/// </summary>
public sealed class VerifyCertificateHandler(ICertificateStore certificates, TimeProvider time)
    : IQueryHandler<VerifyCertificate, Result<CertificateVerificationDto, ApiError>>
{
    public async Task<Result<CertificateVerificationDto, ApiError>> HandleAsync(VerifyCertificate query, CancellationToken cancellationToken)
    {
        Ensure.That(query).IsNotNull();

        if (!VerificationCode.TryFrom(query.Code, out var code))
        {
            return Failure(VerificationFailures.NotVerifiable());
        }

        var certificate = await certificates.FindByVerificationCodeAsync(code, cancellationToken);
        if (!certificate.HasValue)
        {
            return Failure(VerificationFailures.NotVerifiable());
        }

        var today = Timestamp.From(time.GetUtcNow()).DayInGermany();
        return Success(certificate.Value.ToVerificationDto(today));
    }
}
