using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Application.DTOs;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Application.Queries.ListMyCertificates;

/// <summary>One page of the caller's own certificates, newest first (#295).</summary>
public sealed record ListMyCertificates(Guid Caller, Page Page, PageSize PageSize) : IQuery<Result<PagedResult<HeldCertificateDto>, ApiError>>;
