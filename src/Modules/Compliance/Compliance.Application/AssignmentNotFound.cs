using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application;

/// <summary>The pharmacy has no assignment of that id; one of another pharmacy is not found through this one.</summary>
public sealed record AssignmentNotFound(Guid Id)
    : ApiError("compliance.assignment-not-found", "This pharmacy has no such assignment.", HttpStatusCode.NotFound);
