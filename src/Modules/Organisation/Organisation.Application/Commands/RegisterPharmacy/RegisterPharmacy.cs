using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.Commands.RegisterPharmacy;

/// <summary>Registers a pharmacy with the caller as its first manager and answers its id.</summary>
public sealed record RegisterPharmacy(
    PharmacyName Name,
    JobFunction FounderJobFunction,
    PersonId Founder,
    DisplayName FounderName,
    EmailAddress FounderEmail) : ICommand<Result<Guid, ApiError>>;
