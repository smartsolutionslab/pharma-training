using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Domain;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Certificates.Events;

[DeliberatelyUnhandled("TestPassedV1Consumer announces it as CertificateIssuedV1 and requests its PDF with CertificateDocumentRequestedV1 in the same commit; rendering must not be lost, so it is no reaction (docs/adr/0027).")]
public sealed record CertificateIssued(
    CertificateId Certificate,
    PersonId Holder,
    CourseId Course,
    CourseVersionId CourseVersion,
    Timestamp IssuedAt,
    CalendarDate ValidUntil) : IDomainEvent;
