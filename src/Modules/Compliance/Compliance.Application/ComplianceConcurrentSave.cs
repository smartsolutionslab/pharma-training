using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application;

/// <summary>
/// PostgreSQL ended the save as a deadlock or a serialization failure with another save at the same moment. Nothing was
/// saved, and what the caller loaded may still be current, so the same request may simply succeed when tried again; a
/// save whose row version moved on is told that instead, to load again first.
/// </summary>
public sealed record ComplianceConcurrentSave()
    : ApiError("compliance.concurrent-save", "Another change to the assignments was saved at the same moment; try again.", HttpStatusCode.Conflict);
