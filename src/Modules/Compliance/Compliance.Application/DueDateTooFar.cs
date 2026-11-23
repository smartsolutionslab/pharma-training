using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application;

/// <summary>An assignment is due further ahead than any request may set: the caller's mistake, not a plan.</summary>
public sealed record DueDateTooFar(int MaximumYears)
    : ApiError("compliance.due-date-too-far", $"Die Frist liegt mehr als {MaximumYears} Jahre in der Zukunft.", HttpStatusCode.BadRequest);
