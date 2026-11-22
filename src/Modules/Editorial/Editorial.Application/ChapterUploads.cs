using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Storage;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;

/// <summary>
/// Red-test stub for #15 (task A3): issues one name for everything, recognises none and allows no bytes. B4 names each
/// upload <c>course-versions/{version}/{guid v7}.pdf|.mp4</c> and sets the owner's limits.
/// </summary>
public static class ChapterUploads
{
    public const long MaxPdfBytes = 0;

    public const long MaxVideoBytes = 0;

    public static BlobName NameFor(CourseVersionId version, ChapterKind kind)
    {
        Ensure.That(version).IsNotNull();
        return BlobName.From("not-issued");
    }

    public static bool IsIssuedFor(BlobReference file, CourseVersionId version, ChapterKind kind)
    {
        Ensure.That(file).IsNotNull();
        Ensure.That(version).IsNotNull();
        return false;
    }
}
