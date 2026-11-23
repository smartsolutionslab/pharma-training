using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Domain;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Certificates.Events;

[DeliberatelyUnhandled("TestPassedV1Consumer announces it as CertificateIssuedV1 in the same commit; #50 adds the reaction that renders and stores the PDF.")]
public sealed record CertificateIssued(
    CertificateId Certificate,
    PersonId Holder,
    CourseId Course,
    CourseVersionId CourseVersion,
    Timestamp IssuedAt,
    CalendarDate ValidUntil) : IDomainEvent;
