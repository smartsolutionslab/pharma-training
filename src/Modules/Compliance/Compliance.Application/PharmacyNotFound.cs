using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application;

/// <summary>The caller has no membership that has not ended in the pharmacy, and so learns nothing about it.</summary>
public sealed record PharmacyNotFound(Guid Id)
    : ApiError("compliance.pharmacy-not-found", "There is no pharmacy of yours with this id.", HttpStatusCode.NotFound);
