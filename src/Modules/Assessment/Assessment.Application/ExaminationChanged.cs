using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application;

/// <summary>Another write on the examination was stored since this one loaded it (ADR 0016); nothing of this one was stored, and sending it again is safe.</summary>
public sealed record ExaminationChanged(Guid Id)
    : ApiError("assessment.examination-changed", "The examination changed in the meantime; try again.", HttpStatusCode.Conflict);
