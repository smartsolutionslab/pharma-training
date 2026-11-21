using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.DTOs;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.Queries.GetMyPharmacy;

/// <summary>
/// One pharmacy as the person sees it. A pharmacy where they are no active member is not found, so its existence does
/// not leak; the empty Guid is no pharmacy's.
/// </summary>
public sealed record GetMyPharmacy(Guid Id, PersonId Person) : IQuery<Result<MyPharmacyDto, ApiError>>;
