namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Api.Endpoints;

/// <summary>
/// The body of <c>POST /compliance/pharmacies/{id}/assignments</c>: the course, the people to assign it to, the day it is
/// due and, for a recurring course, every how many months it repeats. The assigning manager is the caller.
/// </summary>
public sealed record AssignComplianceCourseRequest(Guid CourseId, IReadOnlyList<Guid>? PersonIds, DateOnly? DueDate, int? RecurrenceMonths);
