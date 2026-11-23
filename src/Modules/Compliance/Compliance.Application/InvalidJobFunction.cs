using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application;

/// <summary>The job function asked for is none the module knows.</summary>
public sealed record InvalidJobFunction()
    : ApiError("compliance.invalid-job-function", "Diese Tätigkeit gibt es nicht.", HttpStatusCode.BadRequest);
