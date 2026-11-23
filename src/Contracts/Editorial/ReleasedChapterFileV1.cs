namespace SmartSolutionsLab.PharmaTraining.Contracts.Editorial;

/// <summary>A chapter's file, as <see cref="ChapterFilesReleasedV1"/> carries it.</summary>
/// <param name="ChapterId">The chapter's identifier.</param>
/// <param name="Kind">What the chapter holds: <see cref="ChapterKindV1.Pdf"/> or <see cref="ChapterKindV1.Video"/>.</param>
/// <param name="ReadUrl">A read URL on Editorial's blob, signed with the kind's content type and an inline disposition; a bearer credential until <paramref name="ExpiresAt"/>.</param>
/// <param name="ExpiresAt">When the storage service starts refusing <paramref name="ReadUrl"/>, at most 15 minutes after signing.</param>
public sealed record ReleasedChapterFileV1(Guid ChapterId, ChapterKindV1 Kind, Uri ReadUrl, DateTimeOffset ExpiresAt);
