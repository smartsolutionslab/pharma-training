using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.DTOs;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Assignments;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Courses;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Progress;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.Queries.ListMyAssignments;

/// <summary>
/// Lists the learner's active assignments in the pharmacy after checking that they are a member there (docs/adr/0003,
/// docs/adr/0015). A learner has a handful of them, so they are resolved, ordered and paged in memory: each state is the
/// assignment's own <see cref="Assignment.StateOn"/> on today's German day, and the version to open follows the running
/// session (docs/adr/0024).
/// </summary>
public sealed class ListMyAssignmentsHandler(
    IMemberStore members,
    IAssignmentStore assignments,
    ICourseVersionStore courses,
    ILearnerProgressStore progress,
    TimeProvider time)
    : IQueryHandler<ListMyAssignments, Result<PagedResult<ComplianceMyAssignmentDto>, ApiError>>
{
    public async Task<Result<PagedResult<ComplianceMyAssignmentDto>, ApiError>> HandleAsync(ListMyAssignments query, CancellationToken cancellationToken)
    {
        Ensure.That(query).IsNotNull();

        var access = await ComplianceAccess.RequireMembershipAsync(members, query.Learner, query.Pharmacy, cancellationToken);
        if (access.IsFailure)
        {
            return Failure(access.Error);
        }

        var today = Timestamp.From(time.GetUtcNow()).DayInGermany();
        var active = await assignments.ActiveOfLearnerInPharmacyAsync(query.Learner, query.Pharmacy, cancellationToken);
        List<CourseId> assigned = [.. active.Select(assignment => assignment.Course).Distinct()];
        var published = (await courses.FindCoursesAsync(assigned, cancellationToken)).ToDictionary(course => course.Course);
        var running = (await progress.OfLearnerAsync(query.Learner, assigned, cancellationToken))
            .ToDictionary(learnerProgress => learnerProgress.Course, learnerProgress => learnerProgress.RunningSession());

        List<MyAssignment> rows =
        [
            .. active
                .Select(assignment => Row(assignment, today, published, running))
                .OrderByDescending(row => row.State == AssignmentState.Overdue)
                .ThenBy(row => row.Assignment.DueDate.Value)
                .ThenBy(row => row.Title.Value, StringComparer.InvariantCultureIgnoreCase)
                .ThenBy(row => row.Assignment.Id.Value),
        ];

        var page = rows.Skip(query.Page.Skip(query.PageSize)).Take(query.PageSize.Value);
        return Success(new PagedResult<ComplianceMyAssignmentDto>([.. page.Select(row => row.ToDto())], rows.Count, query.Page.Value, query.PageSize.Value));
    }

    private static MyAssignment Row(
        Assignment assignment,
        CalendarDate today,
        Dictionary<CourseId, PublishedCourse> published,
        Dictionary<CourseId, Option<LearningSessionStart>> running)
    {
        // An active assignment always has a stored version: assigning refuses a course without one, and the copies are
        // never removed.
        if (!published.TryGetValue(assignment.Course, out var course))
        {
            throw new InvalidOperationException($"Assignment {assignment.Id} is active, but Compliance holds no version of its course {assignment.Course}.");
        }

        var session = running.GetValueOrDefault(assignment.Course, Option<LearningSessionStart>.None);
        return new MyAssignment(assignment, assignment.StateOn(today), course.Title, CourseStart.For(session, course));
    }
}
