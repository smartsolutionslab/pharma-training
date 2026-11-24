using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application;

/// <summary>The request names no pharmacy, although the answer is about one.</summary>
public sealed record PharmacyMissing()
    : ApiError("compliance.pharmacy-missing", "The pharmacy is missing.", HttpStatusCode.BadRequest);
