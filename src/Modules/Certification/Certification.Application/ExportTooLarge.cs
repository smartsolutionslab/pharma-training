using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Application;

/// <summary>An export would hold more certificates than one archive may (#52).</summary>
public sealed record ExportTooLarge()
    : ApiError("certification.export-too-large", "An export holds at most 2000 certificates; choose a shorter period.", HttpStatusCode.BadRequest);
