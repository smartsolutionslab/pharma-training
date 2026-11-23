using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application;

public sealed record CapabilityMissing()
    : ApiError("compliance.capability-missing", "Ihre Rolle in dieser Apotheke erlaubt das nicht.", HttpStatusCode.Forbidden);
