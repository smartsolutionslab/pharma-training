using System.Security.Claims;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Api;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.Commands.AssignCourse;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.DTOs;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Assignments;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Courses;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Members;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Api.Endpoints;

/// <summary>
/// A manager assigns a course to several members of their pharmacy at once. Open to every signed-in person: whether the
/// caller may assign in that pharmacy is decided by the handler from Compliance's copy of their membership
/// (docs/adr/0003). One request creates none to many assignments, so it answers 200 with each learner assigned or
/// refused, never 201.
/// </summary>
public sealed class AssignmentEndpoints : IEndpoint
{
    /// <summary>The most person ids, repeats included, a body may carry before it is refused unread.</summary>
    private const int MaximumPersonIds = 10 * AssignCourse.MaximumLearners;

    public void Map(IEndpointRouteBuilder endpoints)
    {
        Ensure.That(endpoints).IsNotNull();

        endpoints.MapPost("/pharmacies/{id:guid}/assignments", AssignAsync).RequireAuthorization();
    }

    private static async Task<Results<Ok<ComplianceCourseAssignmentDto>, ProblemHttpResult>> AssignAsync(
        Guid id,
        AssignComplianceCourseRequest request,
        ClaimsPrincipal user,
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        var caller = ComplianceCaller.PersonOf(user);
        if (!caller.HasValue)
        {
            return Refused(AssignmentFailures.IdentityIncomplete());
        }

        // A body far beyond the cap is refused before anything else reads it or maps its ids one by one. Repeats count
        // here, as they are read; the handler's own cap counts distinct people.
        if (request.PersonIds is { Count: > MaximumPersonIds })
        {
            return Refused(AssignmentFailures.TooManyLearners(AssignCourse.MaximumLearners));
        }

        // The empty Guid is no pharmacy and no course Compliance holds; each is not found like any other.
        if (id == Guid.Empty)
        {
            return Refused(AssignmentFailures.PharmacyNotFound(id));
        }

        if (request.CourseId == Guid.Empty)
        {
            return Refused(AssignmentFailures.CourseNotFound(request.CourseId));
        }

        // What the caller got wrong is refused before it becomes a value object, whose factory would throw on it.
        if (request.PersonIds is null)
        {
            return Refused(AssignmentFailures.NoLearners());
        }

        if (request.PersonIds.Any(person => person == Guid.Empty))
        {
            return Refused(AssignmentFailures.InvalidPerson());
        }

        // A body without a due date is not read as 0001-01-01 and then told the date is past.
        if (request.DueDate is not { } dueDate)
        {
            return Refused(AssignmentFailures.DueDateMissing());
        }

        RecurrenceInterval? recurrence = null;
        if (request.RecurrenceMonths is { } months && !RecurrenceInterval.TryFrom(months, out recurrence))
        {
            return Refused(AssignmentFailures.InvalidRecurrence());
        }

        var command = new AssignCourse(
            PharmacyId.From(id),
            CourseId.From(request.CourseId),
            [.. request.PersonIds.Select(PersonId.From)],
            DueDate.From(dueDate),
            recurrence,
            caller.Value);
        return (await dispatcher.SendAsync(command, cancellationToken)).ToHttpResult();
    }

    private static Results<Ok<ComplianceCourseAssignmentDto>, ProblemHttpResult> Refused(ApiError error) =>
        Result<ComplianceCourseAssignmentDto, ApiError>.Failure(error).ToHttpResult();
}
