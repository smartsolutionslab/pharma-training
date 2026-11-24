using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application;

public sealed record CapabilityMissing()
    : ApiError("organisation.capability-missing", "Your role in this pharmacy does not allow this.", HttpStatusCode.Forbidden);
