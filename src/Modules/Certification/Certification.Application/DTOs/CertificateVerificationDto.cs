namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Application.DTOs;

/// <summary>
/// What anyone with a certificate's code is shown (#51, Z2): the holder's name and the course title as issued, the
/// German day of issue, the last valid day and the validity today. Nothing else: no id, score, pharmacy or code.
/// </summary>
public sealed record CertificateVerificationDto(
    string HolderName,
    string CourseTitle,
    DateOnly IssuedOn,
    DateOnly ValidUntil,
    CertificateValidity Validity);
