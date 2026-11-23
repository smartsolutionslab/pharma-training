using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.DTOs;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Assignments;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Courses;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Members;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.Commands.AssignCourse;

/// <summary>
/// A member who may assign courses in the pharmacy assigns the course to the learners, due on the day and perhaps
/// recurring. Answers, per learner, the assignment created or why none was.
/// </summary>
public sealed record AssignCourse(PharmacyId Pharmacy, CourseId Course, IReadOnlyList<PersonId> Learners, DueDate DueDate, RecurrenceInterval? Recurrence, PersonId By)
    : ICommand<Result<ComplianceCourseAssignmentDto, ApiError>>
{
    /// <summary>The most distinct learners one request assigns.</summary>
    public const int MaximumLearners = 100;
}
