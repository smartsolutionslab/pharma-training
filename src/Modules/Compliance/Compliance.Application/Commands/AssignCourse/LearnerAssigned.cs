using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Assignments;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Members;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.Commands.AssignCourse;

/// <summary>The learner was assigned the course by the assignment.</summary>
public sealed record LearnerAssigned(PersonId Learner, AssignmentId Assignment);
