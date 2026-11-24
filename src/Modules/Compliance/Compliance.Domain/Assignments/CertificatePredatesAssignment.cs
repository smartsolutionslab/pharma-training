namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Assignments;

public sealed record CertificatePredatesAssignment(AssignmentId Assignment, CertificateId Certificate)
    : DomainError("compliance.certificate-predates-assignment", "A certificate from before the assignment does not fulfil it.");
