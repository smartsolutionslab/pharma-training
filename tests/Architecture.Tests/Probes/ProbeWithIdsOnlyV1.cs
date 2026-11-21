using SmartSolutionsLab.PharmaTraining.Contracts;

namespace SmartSolutionsLab.PharmaTraining.Architecture.Tests.Probes;

/// <summary>An event that names a person only by its id, as every event of Learning and Assessment does.</summary>
public sealed record ProbeWithIdsOnlyV1(Guid SessionId, Guid PersonId, Guid CourseId, IReadOnlyList<Guid> ChapterIds, int Score) : IntegrationEvent;
