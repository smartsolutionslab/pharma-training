using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.Commands.RegisterPharmacy;

/// <summary>
/// Registers a pharmacy with the caller as its first manager and answers its id. Only a caller whose address Keycloak
/// verified may register one.
/// </summary>
public sealed record RegisterPharmacy(
    PharmacyName Name,
    JobFunction FounderJobFunction,
    PersonId Founder,
    DisplayName FounderName,
    AccountEmail FounderEmail) : ICommand<Result<Guid, ApiError>>;
