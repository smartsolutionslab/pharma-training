using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application;

/// <summary>An assignment names a person by an empty id.</summary>
public sealed record InvalidPerson()
    : ApiError("compliance.invalid-person", "Eine der Personen hat keine gültige Kennung.", HttpStatusCode.BadRequest);
