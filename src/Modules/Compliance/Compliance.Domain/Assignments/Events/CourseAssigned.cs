using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Domain;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Courses;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Members;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Assignments.Events;

[DeliberatelyUnhandled("The command that assigns and the follow-up consumer announce it to other modules as CourseAssignedV1; nothing in Compliance reacts.")]
public sealed record CourseAssigned(AssignmentId Assignment, PersonId Learner, PharmacyId Pharmacy, CourseId Course, DueDate DueDate) : IDomainEvent;
