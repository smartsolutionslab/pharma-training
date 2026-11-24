using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application;

/// <summary>A manager asked to move or withdraw an assignment of their own; another manager of the pharmacy must.</summary>
public sealed record OwnAssignment()
    : ApiError("compliance.own-assignment", "Only another manager of the pharmacy can change or withdraw your own assignment.", HttpStatusCode.Forbidden);
