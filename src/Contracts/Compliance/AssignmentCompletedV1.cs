namespace SmartSolutionsLab.PharmaTraining.Contracts.Compliance;

/// <summary>Compliance completed an assignment with a certificate, on time or late; no consumer in MVP 1.</summary>
public sealed record AssignmentCompletedV1(Guid AssignmentId, Guid PersonId, Guid PharmacyId, Guid CourseId, Guid CertificateId, DateTimeOffset CompletedAt, bool Late) : IntegrationEvent;
