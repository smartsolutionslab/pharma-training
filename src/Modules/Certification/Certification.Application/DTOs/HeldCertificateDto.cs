namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Application.DTOs;

/// <summary>
/// One of a person's own certificates as they list them (#295): its id, the course title as issued, the German day of
/// issue, the last valid day, the validity today and whether its PDF is stored, so it can be opened.
/// </summary>
public sealed record HeldCertificateDto(
    Guid CertificateId,
    string CourseTitle,
    DateOnly IssuedOn,
    DateOnly ValidUntil,
    CertificateValidity Validity,
    bool DocumentReady);
