namespace SmartSolutionsLab.PharmaTraining.Contracts.Compliance;

/// <summary>
/// Compliance has done its part of a person's erasure (#115): the person is tombstoned, their name is gone from
/// Compliance's copies of the memberships, their open assignments are cancelled and the messages that held their name
/// are purged. Organisation records it as the erasure's step <c>compliance</c>. It carries the erasure's id only.
/// </summary>
public sealed record ComplianceErasureCompletedV1(Guid ErasureId) : IntegrationEvent;
