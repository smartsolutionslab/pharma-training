using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Application.DTOs;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Application.Queries.GetRetentionPeriod;

/// <summary>The retention period of the pharmacy, for its manager (#391).</summary>
public sealed record GetRetentionPeriod(Guid PharmacyId, Guid Caller) : IQuery<Result<CertificationRetentionPeriodDto, ApiError>>;
