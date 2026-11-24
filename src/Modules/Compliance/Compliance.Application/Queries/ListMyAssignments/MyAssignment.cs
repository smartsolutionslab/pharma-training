using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Assignments;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Courses;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Progress;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.Queries.ListMyAssignments;

/// <summary>One of the learner's active assignments with its state today, its course's latest title and the version to open, if any.</summary>
public sealed record MyAssignment(Assignment Assignment, AssignmentState State, CourseTitle Title, Option<CourseStart> Start);
