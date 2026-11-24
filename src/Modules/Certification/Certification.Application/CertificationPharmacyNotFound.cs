using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Application;

/// <summary>The caller has no active membership in the pharmacy: an unknown pharmacy and someone else's answer alike (docs/adr/0003).</summary>
public sealed record CertificationPharmacyNotFound(Guid Id)
    : ApiError("certification.pharmacy-not-found", "You are not a member of this pharmacy, or it does not exist.", HttpStatusCode.NotFound);
