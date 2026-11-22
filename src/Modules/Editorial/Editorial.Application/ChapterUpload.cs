using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Storage;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;

/// <summary>An upload issued for a chapter's file: the name the module chose, its signed URL, and what the blob must be.</summary>
public sealed record ChapterUpload(BlobName Name, BlobUrl Url, string ContentType, long MaxBytes);
