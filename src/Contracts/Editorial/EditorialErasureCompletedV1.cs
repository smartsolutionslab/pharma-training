namespace SmartSolutionsLab.PharmaTraining.Contracts.Editorial;

/// <summary>
/// Editorial has done its part of a person's erasure (#115): their name is gone from the questions they approved and
/// the versions they returned. Organisation records it as the erasure's step <c>editorial</c>. It carries the erasure's
/// id only.
/// </summary>
public sealed record EditorialErasureCompletedV1(Guid ErasureId) : IntegrationEvent;
