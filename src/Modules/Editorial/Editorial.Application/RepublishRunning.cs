using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;

/// <summary>Another republish of the course versions is running; this one sent nothing (ADR 0019).</summary>
public sealed record RepublishRunning()
    : ApiError(
        "editorial.republish-running",
        "A republish of the course versions is running already; wait until it has finished, then run it again if needed.",
        HttpStatusCode.Conflict);
