using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.DTOs;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.Queries.GetErasure;

/// <summary>One erasure from the log, with its steps; the empty Guid is no erasure's.</summary>
public sealed record GetErasure(Guid Id) : IQuery<Result<OrganisationErasureDto, ApiError>>;
