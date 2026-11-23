using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application;

/// <summary>A manager asked to move or withdraw an assignment of their own; another manager of the pharmacy must.</summary>
public sealed record OwnAssignment()
    : ApiError("compliance.own-assignment", "Ihre eigene Zuweisung kann nur eine andere Leitung der Apotheke ändern oder zurückziehen.", HttpStatusCode.Forbidden);
