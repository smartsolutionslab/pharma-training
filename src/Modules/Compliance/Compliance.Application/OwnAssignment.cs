using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application;

/// <summary>A manager asked to move or withdraw an assignment of their own while another manager of the pharmacy can (#284).</summary>
public sealed record OwnAssignment()
    : ApiError("compliance.own-assignment", "Ihre eigene Zuweisung kann nur eine andere Leitung der Apotheke ändern oder zurückziehen.", HttpStatusCode.Forbidden);
