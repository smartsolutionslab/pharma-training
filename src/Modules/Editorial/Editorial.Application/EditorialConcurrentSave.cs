using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;

/// <summary>
/// PostgreSQL ended the save as a deadlock or a serialization failure with another save at the same moment. Nothing was
/// saved, and what the caller loaded may still be current, so the same request may simply succeed when tried again; a
/// save whose row version moved on is told that instead, to load again first.
/// </summary>
public sealed record EditorialConcurrentSave()
    : ApiError("editorial.concurrent-save", "Another change to the course version was saved at the same moment; try again.", HttpStatusCode.Conflict);
