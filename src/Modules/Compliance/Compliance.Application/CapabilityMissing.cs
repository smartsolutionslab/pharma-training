using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application;

public sealed record CapabilityMissing()
    : ApiError("compliance.capability-missing", "Your role in this pharmacy does not allow this.", HttpStatusCode.Forbidden);
