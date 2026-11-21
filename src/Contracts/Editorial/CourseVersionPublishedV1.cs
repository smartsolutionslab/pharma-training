namespace SmartSolutionsLab.PharmaTraining.Contracts.Editorial;

/// <summary>Editorial published a course version, with its chapters but no questions; consumed by Catalog, Learning, Certification and Compliance.</summary>
public sealed record CourseVersionPublishedV1(Guid CourseId, Guid CourseVersionId, int VersionNumber, string Title, int ValidityMonths, IReadOnlyList<CourseChapterV1> Chapters) : IntegrationEvent;
