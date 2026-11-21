using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application;

/// <summary>Another change to the pharmacy was saved after this one loaded it; nothing of this one was saved.</summary>
public sealed record PharmacyChanged(Guid Id)
    : ApiError("organisation.pharmacy-changed", $"Die Apotheke {Id} wurde inzwischen geändert; laden Sie sie neu und wiederholen Sie die Änderung.", HttpStatusCode.Conflict);
