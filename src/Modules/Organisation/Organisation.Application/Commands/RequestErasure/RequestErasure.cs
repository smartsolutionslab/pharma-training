using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.Commands.RequestErasure;

/// <summary>Support requests the erasure of a person's data (GDPR Art. 17, #115); answers the erasure's id.</summary>
public sealed record RequestErasure(PersonId Person, PersonId By) : ICommand<Result<Guid, ApiError>>;
