using System.Net;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Http;

/// <summary>
/// A unique index or the event store refused a second writer of one name or key: the host's generic <c>409</c>, the
/// backstop behind a use case's own uniqueness check.
/// </summary>
public sealed record AlreadyExists()
    : ApiError("already-exists", "Something with that name or key already exists.", HttpStatusCode.Conflict);
