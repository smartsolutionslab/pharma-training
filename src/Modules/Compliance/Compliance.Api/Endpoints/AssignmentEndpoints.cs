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
    /// <summary>
    /// The most person ids, repeats included, a body may carry before it is refused without mapping its ids one by one.
    /// The body has been read and bound by then; <see cref="AssignRequestLimit"/> is what refuses a larger one unread.
    /// </summary>
    private const int MaximumPersonIds = 10 * AssignCourse.MaximumLearners;

    /// <summary>
    /// The most bytes the server reads of an assign request's body; a larger one is refused (413) before it is bound. A
    /// Guid in JSON is about 39 bytes with its quotes and comma, so <see cref="MaximumPersonIds"/> ids need about 39 KB
    /// and the rest of the body a few bytes more: 64 KB leaves headroom for them, while three thousand ids (about
    /// 117 KB) do not fit.
    /// </summary>
    private const int AssignRequestLimit = 65_536;

    public void Map(IEndpointRouteBuilder endpoints)
    {
        Ensure.That(endpoints).IsNotNull();

        endpoints.MapPost("/pharmacies/{id:guid}/assignments", AssignAsync)
            .WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(AssignRequestLimit))
            .RequireAuthorization();
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

        // A body far beyond the cap is refused before anything maps its ids one by one. Repeats count here, as they
        // are bound; the handler's own cap counts distinct people.
        if (request.PersonIds is { Count: > MaximumPersonIds })
        {
            return Refused(AssignmentFailures.TooManyLearners());
        }

        // The empty Guid is no pharmacy and no course Compliance holds; each is not found like any other.
        if (id == Guid.Empty)
        {
            return Refused(AssignmentFailures.PharmacyNotFound(id));
        }

        // A body without a course is not read as the empty id and told that course does not exist.
        if (request.CourseId is not { } courseId)
        {
            return Refused(AssignmentFailures.CourseMissing());
        }

        if (courseId == Guid.Empty)
        {
            return Refused(AssignmentFailures.CourseNotFound(courseId));
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
            CourseId.From(courseId),
            [.. request.PersonIds.Select(PersonId.From)],
            DueDate.From(dueDate),
            recurrence,
            caller.Value);
        return (await dispatcher.SendAsync(command, cancellationToken)).ToHttpResult();
    }

    private static Results<Ok<ComplianceCourseAssignmentDto>, ProblemHttpResult> Refused(ApiError error) =>
        Result<ComplianceCourseAssignmentDto, ApiError>.Failure(error).ToHttpResult();
}
