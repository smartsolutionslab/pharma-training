using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application;

/// <summary>The caller's token names no person Compliance can use.</summary>
public sealed record IdentityIncomplete()
    : ApiError("compliance.identity-incomplete", "The account names no valid person.", HttpStatusCode.Forbidden);
