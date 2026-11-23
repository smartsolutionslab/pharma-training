using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Members;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.Commands.AssignCourse;

/// <summary>The learner was not assigned the course, for the reason.</summary>
public sealed record LearnerRefused(PersonId Learner, ApiError Reason);
