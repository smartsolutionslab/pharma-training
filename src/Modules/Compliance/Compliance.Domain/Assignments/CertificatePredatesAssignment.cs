namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Assignments;

public sealed record CertificatePredatesAssignment(AssignmentId Assignment, CertificateId Certificate)
    : DomainError("compliance.certificate-predates-assignment", "Eine Bestätigung von vor der Zuweisung erfüllt sie nicht.");
