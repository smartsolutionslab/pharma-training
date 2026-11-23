using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Domain;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Members;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Assignments.Events;

[DeliberatelyUnhandled("Nobody outside Compliance reacts in MVP 1.")]
public sealed record AssignmentCancelled(AssignmentId Assignment, PersonId Learner, PharmacyId Pharmacy) : IDomainEvent;
