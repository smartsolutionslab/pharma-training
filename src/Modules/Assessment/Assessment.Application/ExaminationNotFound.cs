using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application;

/// <summary>No examination of the caller's: an unknown one and another person's are answered alike, since examination ids are derivable.</summary>
public sealed record ExaminationNotFound(Guid Id)
    : ApiError("assessment.examination-not-found", $"There is no examination {Id} of yours.", HttpStatusCode.NotFound);
