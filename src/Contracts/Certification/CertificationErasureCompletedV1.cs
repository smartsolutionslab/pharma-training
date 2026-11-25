namespace SmartSolutionsLab.PharmaTraining.Contracts.Certification;

/// <summary>
/// Certification has done its part of a person's erasure (#115): the person is tombstoned, the certificates no pharmacy
/// keeps any longer are deleted with their PDFs, their name is gone from Certification's copies of the memberships and
/// the messages that held it are purged. Organisation records it as the erasure's step <c>certification</c>. It carries
/// the erasure's id only.
/// </summary>
public sealed record CertificationErasureCompletedV1(Guid ErasureId) : IntegrationEvent;
