namespace SmartSolutionsLab.PharmaTraining.Contracts.Learning;

/// <summary>
/// Learning has done its part of a person's erasure (#115): the membership messages it stored that held their name are
/// purged. Organisation records it as the erasure's step <c>learning</c>. It carries the erasure's id only.
/// </summary>
public sealed record LearningErasureCompletedV1(Guid ErasureId) : IntegrationEvent;
