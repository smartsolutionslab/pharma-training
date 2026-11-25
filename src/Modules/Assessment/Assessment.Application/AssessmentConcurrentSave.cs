using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application;

/// <summary>
/// PostgreSQL ended the save as a deadlock or a serialization failure with another save at the same moment. Nothing was
/// stored, and what the caller loaded may still be current, so the same request may simply succeed when tried again; a
/// stream another append moved on is told that instead, to load again first.
/// </summary>
public sealed record AssessmentConcurrentSave()
    : ApiError("assessment.concurrent-save", "Another change to the examination was saved at the same moment; try again.", HttpStatusCode.Conflict);
