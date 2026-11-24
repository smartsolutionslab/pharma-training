using System.Net;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Http;

/// <summary>
/// A guard rejected the caller's input (<see cref="GuardException"/>): the host's generic <c>400</c>. The problem's
/// <c>detail</c> carries the guard's own message; this is its code, title and status.
/// </summary>
public sealed record InvalidArgument()
    : ApiError("invalid-argument", "The request is invalid.", HttpStatusCode.BadRequest);
