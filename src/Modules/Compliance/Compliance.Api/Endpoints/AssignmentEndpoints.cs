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

        RecurrenceInterval? recurrence = null;
        if (request.RecurrenceMonths is { } months && !RecurrenceInterval.TryFrom(months, out recurrence))
        {
            return Refused(AssignmentFailures.InvalidRecurrence());
        }

        var command = new AssignCourse(
            PharmacyId.From(id),
            CourseId.From(request.CourseId),
            [.. request.PersonIds.Select(PersonId.From)],
            DueDate.From(request.DueDate),
            recurrence,
            caller.Value);
        return (await dispatcher.SendAsync(command, cancellationToken)).ToHttpResult();
    }

    private static Results<Ok<ComplianceCourseAssignmentDto>, ProblemHttpResult> Refused(ApiError error) =>
        Result<ComplianceCourseAssignmentDto, ApiError>.Failure(error).ToHttpResult();
}
