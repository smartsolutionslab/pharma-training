namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.DTOs;

/// <summary>
/// Where the browser uploads a chapter's PDF or video: a PUT to <c>UploadUrl</c> with <c>x-ms-blob-type: BlockBlob</c>
/// and <c>Content-Type: ContentType</c>, of at most <c>MaxBytes</c>, before <c>ExpiresAt</c>; the chapter then names
/// <c>BlobName</c> as its file.
/// </summary>
public sealed record EditorialChapterUploadDto(string BlobName, Uri UploadUrl, DateTimeOffset ExpiresAt, string ContentType, long MaxBytes);
