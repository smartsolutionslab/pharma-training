using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Courses;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Members;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Assignments;

public sealed record AssignmentAlreadyActive(PersonId Learner, CourseId Course)
    : DomainError("compliance.assignment-already-active", "Für diese Schulung gibt es schon eine offene Zuweisung.");
