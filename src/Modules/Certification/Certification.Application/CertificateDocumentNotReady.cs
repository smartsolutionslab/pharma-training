using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Application;

public sealed record CertificateDocumentNotReady(Guid Id)
    : ApiError("certification.document-not-ready", $"The PDF of certificate {Id} is being created; ask again shortly.", HttpStatusCode.Conflict);
