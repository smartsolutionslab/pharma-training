using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Domain;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Assignments.Events;

[DeliberatelyUnhandled("Only the dashboard read model cares; no integration event in MVP 1.")]
public sealed record AssignmentDueDateChanged(AssignmentId Assignment, DueDate NewDueDate) : IDomainEvent;
