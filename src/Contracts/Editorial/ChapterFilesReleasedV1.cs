namespace SmartSolutionsLab.PharmaTraining.Contracts.Editorial;

/// <summary>
/// Editorial released the files of a published course version's PDF and video chapters, published in the same unit of
/// work as <see cref="CourseVersionPublishedV1"/> when the version has at least one; consumed by Learning, which copies
/// each file into its own container (docs/adr/0002). Each <see cref="ReleasedChapterFileV1.ReadUrl"/> is a bearer
/// credential for Editorial's blob, valid for at most 15 minutes after signing; it is worthless after
/// <see cref="ReleasedChapterFileV1.ExpiresAt"/>.
/// </summary>
/// <param name="CourseVersionId">The published course version the chapters belong to.</param>
/// <param name="Files">One entry per PDF or video chapter of the version.</param>
public sealed record ChapterFilesReleasedV1(Guid CourseVersionId, IReadOnlyList<ReleasedChapterFileV1> Files) : IntegrationEvent;
