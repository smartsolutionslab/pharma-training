using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.DTOs;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Members;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.Queries.GetComplianceMatrix;

/// <summary>
/// The team matrix of a pharmacy: its active members, a page at a time, by the courses assigned there, with the
/// compliance rate over every row, optionally only of members whose current job function is the one given; asked by a
/// member whose current role may view the team's compliance there.
/// </summary>
public sealed record GetComplianceMatrix(PharmacyId Pharmacy, PersonId By, JobFunction? JobFunction, Page Page, PageSize PageSize)
    : IQuery<Result<ComplianceMatrixDto, ApiError>>;
