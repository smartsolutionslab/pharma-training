using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application;

/// <summary>The reason given for a change is longer than the history keeps.</summary>
public sealed record InvalidChangeReason(int MaxLength)
    : ApiError("compliance.invalid-change-reason", $"Der Grund darf höchstens {MaxLength} Zeichen lang sein.", HttpStatusCode.BadRequest);
