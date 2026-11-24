using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application;

/// <summary>The job function asked for is none the module knows.</summary>
public sealed record InvalidJobFunction()
    : ApiError("compliance.invalid-job-function", "There is no such job function.", HttpStatusCode.BadRequest);
