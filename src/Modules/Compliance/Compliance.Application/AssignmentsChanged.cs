using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application;

/// <summary>Another save changed the assignments at the same time; nothing of this one was saved.</summary>
public sealed record AssignmentsChanged()
    : ApiError("compliance.assignments-changed", "Assignments were changed at the same time; try again.", HttpStatusCode.Conflict);
