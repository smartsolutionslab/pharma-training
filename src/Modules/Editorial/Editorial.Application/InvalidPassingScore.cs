using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;

// Red-first stub for #17: code, message and status are wrong on purpose until the change lands.
public sealed record InvalidPassingScore()
    : ApiError("stub", "stub", HttpStatusCode.InternalServerError);
