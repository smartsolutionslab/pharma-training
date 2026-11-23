using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Application;

/// <summary>Another report on the session was stored since this one loaded it; reporting again is safe.</summary>
public sealed record LearningSessionChanged(Guid Id)
    : ApiError("learning.session-changed", $"The learning session {Id} changed in the meantime; report again.", HttpStatusCode.Conflict);
