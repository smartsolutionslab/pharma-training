using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application;

/// <summary>An assignment names no day it is due.</summary>
public sealed record DueDateMissing()
    : ApiError("compliance.due-date-missing", "The due date is missing.", HttpStatusCode.BadRequest);
