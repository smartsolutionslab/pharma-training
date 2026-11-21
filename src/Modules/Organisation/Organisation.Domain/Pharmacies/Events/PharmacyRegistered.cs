using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Domain;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies.Events;

[DeliberatelyUnhandled("The command that registers the pharmacy announces it to other modules as PharmacyRegisteredV1; nothing in Organisation reacts.")]
public sealed record PharmacyRegistered(PharmacyId Pharmacy, PharmacyName Name) : IDomainEvent;
