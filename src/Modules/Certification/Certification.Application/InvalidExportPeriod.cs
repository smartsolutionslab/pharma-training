using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Application;

/// <summary>An export's period lacks a day, ends before it starts, or spans more than five years (#52).</summary>
public sealed record InvalidExportPeriod()
    : ApiError("certification.invalid-export-period", "An export needs from and to, with to not before from and at most five years after it.", HttpStatusCode.BadRequest);
