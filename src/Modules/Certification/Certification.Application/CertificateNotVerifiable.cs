using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Application;

/// <summary>Every code that finds no certificate, malformed or not; it never names the code.</summary>
public sealed record CertificateNotVerifiable()
    : ApiError("certification.certificate-not-verifiable", "No certificate matches this code.", HttpStatusCode.NotFound);
