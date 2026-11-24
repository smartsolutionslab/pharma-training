using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Application;

/// <summary>The caller is a member of the pharmacy, but their current role there does not grant what they asked for (docs/adr/0003).</summary>
public sealed record CertificationCapabilityMissing()
    : ApiError("certification.capability-missing", "Your role in this pharmacy does not allow this.", HttpStatusCode.Forbidden);
