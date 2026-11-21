using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application;

public sealed record CapabilityMissing()
    : ApiError("organisation.capability-missing", "Ihre Rolle in dieser Apotheke erlaubt das nicht.", HttpStatusCode.Forbidden);
