using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application;

/// <summary>An assignment names no one to assign the course to.</summary>
public sealed record NoLearners()
    : ApiError("compliance.no-learners", "Choose at least one person.", HttpStatusCode.BadRequest);
