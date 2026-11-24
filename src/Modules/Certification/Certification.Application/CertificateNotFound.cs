using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Application;

/// <summary>No certificate of the caller has the id: an unknown id and someone else's certificate answer alike.</summary>
public sealed record CertificateNotFound(Guid Id)
    : ApiError("certification.certificate-not-found", $"You hold no certificate {Id}.", HttpStatusCode.NotFound);
