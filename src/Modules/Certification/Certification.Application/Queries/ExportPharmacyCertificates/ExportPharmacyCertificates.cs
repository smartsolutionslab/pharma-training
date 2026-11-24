using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Application.Exports;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Application.Queries.ExportPharmacyCertificates;

/// <summary>A pharmacy's certificates issued from <paramref name="From"/> through <paramref name="To"/>, as a ZIP for its manager (#52).</summary>
public sealed record ExportPharmacyCertificates(Guid PharmacyId, Guid Caller, DateOnly? From, DateOnly? To) : IQuery<Result<CertificateExportArchive, ApiError>>;
