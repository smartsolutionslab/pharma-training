using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Application;

/// <summary>An address over the verification limit (docs/adr/0029).</summary>
public sealed record VerificationLimitReached()
    : ApiError("certification.verification-limit-reached", "Too many verifications from this address; try again later.", HttpStatusCode.TooManyRequests);
