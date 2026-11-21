namespace SmartSolutionsLab.PharmaTraining.Contracts.Certification;

/// <summary>Certification issued a person's certificate for a course version, valid until a day; consumed by Compliance.</summary>
public sealed record CertificateIssuedV1(Guid CertificateId, Guid PersonId, Guid CourseId, Guid CourseVersionId, DateTimeOffset IssuedAt, DateOnly ValidUntil) : IntegrationEvent;
