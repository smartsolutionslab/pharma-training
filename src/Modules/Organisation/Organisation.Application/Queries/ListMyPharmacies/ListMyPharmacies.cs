using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.DTOs;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.Queries.ListMyPharmacies;

/// <summary>One page of the pharmacies where the person is an active member, by name.</summary>
public sealed record ListMyPharmacies(PersonId Person, Page Page, PageSize PageSize) : IQuery<Result<PagedResult<MyPharmacyDto>, ApiError>>;
