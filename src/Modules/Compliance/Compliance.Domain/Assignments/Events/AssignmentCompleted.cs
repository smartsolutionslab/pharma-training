using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Domain;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Courses;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Members;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Assignments.Events;

[DeliberatelyUnhandled("The CertificateIssuedV1 consumer announces it as AssignmentCompletedV1 in the completing commit; #31 replaces this with the follow-up reaction.")]
public sealed record AssignmentCompleted(
    AssignmentId Assignment,
    PersonId Learner,
    PharmacyId Pharmacy,
    CourseId Course,
    CertificateId Certificate,
    Timestamp CompletedAt,
    DueDate DueDate) : IDomainEvent;
