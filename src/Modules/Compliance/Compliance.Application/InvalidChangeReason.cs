using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application;

/// <summary>The reason given for a change is longer than the history keeps, or holds control or format characters.</summary>
public sealed record InvalidChangeReason(int MaxLength)
    : ApiError("compliance.invalid-change-reason", $"Der Grund darf höchstens {MaxLength} Zeichen lang sein und keine Steuer- oder Formatzeichen enthalten.", HttpStatusCode.BadRequest);
