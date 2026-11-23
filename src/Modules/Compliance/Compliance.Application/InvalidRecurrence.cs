using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application;

/// <summary>A recurrence outside one to sixty months.</summary>
public sealed record InvalidRecurrence(int Minimum, int Maximum)
    : ApiError("compliance.invalid-recurrence", $"Die Wiederholung muss zwischen {Minimum} und {Maximum} Monaten liegen.", HttpStatusCode.BadRequest);
