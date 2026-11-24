using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Domain;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Courses;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Members;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Assignments.Events;

[DeliberatelyUnhandled("The CertificateIssuedV1 consumer announces it as AssignmentCompletedV1 and, for a recurring assignment, requests its follow-up as AssignmentFollowUpRequestedV1 in the completing commit, so neither is lost (AGENTS.md; docs/adr/0026).")]
public sealed record AssignmentCompleted(
    AssignmentId Assignment,
    PersonId Learner,
    PharmacyId Pharmacy,
    CourseId Course,
    CertificateId Certificate,
    Timestamp CompletedAt,
    DueDate DueDate) : IDomainEvent;
