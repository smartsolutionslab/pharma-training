using System.Net;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Assignments;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application;

/// <summary>A recurrence outside one to sixty months.</summary>
public sealed record InvalidRecurrence()
    : ApiError("compliance.invalid-recurrence", $"A recurrence is {RecurrenceInterval.MinimumMonths} to {RecurrenceInterval.MaximumMonths} months.", HttpStatusCode.BadRequest);
