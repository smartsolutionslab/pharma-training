namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.DTOs;

/// <summary>An issued upload as the module's endpoint answers it.</summary>
public static class ChapterUploadMappings
{
    public static EditorialChapterUploadDto ToDto(this ChapterUpload upload)
    {
        Ensure.That(upload).IsNotNull();
        return new EditorialChapterUploadDto(upload.Name.Value, upload.Url.Url, upload.Url.ExpiresAt, upload.ContentType, upload.MaxBytes);
    }
}
