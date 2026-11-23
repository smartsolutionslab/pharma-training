using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application;

/// <summary>The caller's token names no person Compliance can use.</summary>
public sealed record IdentityIncomplete()
    : ApiError("compliance.identity-incomplete", "Das Konto liefert keine gültige Person.", HttpStatusCode.Forbidden);
