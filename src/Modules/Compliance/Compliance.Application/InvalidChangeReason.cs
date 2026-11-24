using System.Net;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Assignments;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application;

/// <summary>The reason given for a change is longer than the history keeps, or holds control or format characters.</summary>
public sealed record InvalidChangeReason()
    : ApiError("compliance.invalid-change-reason", $"A reason is at most {ChangeReason.MaxLength} characters and holds no control or format characters.", HttpStatusCode.BadRequest);
