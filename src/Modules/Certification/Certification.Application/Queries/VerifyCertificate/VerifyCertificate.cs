using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Application.DTOs;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Application.Queries.VerifyCertificate;

/// <summary>The certificate a QR code's <paramref name="Code"/> names, as anyone may see it; any input, well formed or not.</summary>
public sealed record VerifyCertificate(string Code) : IQuery<Result<CertificateVerificationDto, ApiError>>;
