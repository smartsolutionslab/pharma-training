using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Courses;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Members;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Assignments;

public sealed record AssignmentAlreadyActive(PersonId Learner, CourseId Course)
    : DomainError("compliance.assignment-already-active", "The person already has an open assignment of this course.");
