using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application;

/// <summary>An assignment names a person by an empty id.</summary>
public sealed record InvalidPerson()
    : ApiError("compliance.invalid-person", "One of the persons has no valid identifier.", HttpStatusCode.BadRequest);
