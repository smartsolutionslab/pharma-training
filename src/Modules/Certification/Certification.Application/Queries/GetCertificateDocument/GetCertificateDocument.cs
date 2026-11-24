using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Application.DTOs;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Application.Queries.GetCertificateDocument;

/// <summary>A short-lived read URL on a certificate's stored PDF, for its holder alone (#50).</summary>
public sealed record GetCertificateDocument(Guid CertificateId, Guid Caller) : IQuery<Result<CertificationDocumentUrlDto, ApiError>>;
