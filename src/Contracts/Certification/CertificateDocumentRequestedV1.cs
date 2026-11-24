namespace SmartSolutionsLab.PharmaTraining.Contracts.Certification;

/// <summary>
/// Certification issued a certificate and its PDF is due. Certification's own work item: its own consumer renders the
/// PDF once and stores it. It never leaves the Certification service (a module of the host consumes it, so Wolverine
/// routes it to the host's durable local queue and never to the broker), so no other module can consume it. It carries
/// the certificate's id only, never a name or a title, so its envelope and its dead letter hold no personal data
/// (#50, docs/adr/0030).
/// </summary>
public sealed record CertificateDocumentRequestedV1(Guid CertificateId) : IntegrationEvent;
