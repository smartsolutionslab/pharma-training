using SmartSolutionsLab.PharmaTraining.Modules.Catalog.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Catalog.Application.DTOs;

public static class CourseVersionMappings
{
    public static CatalogCourseDto ToCatalogCourseDto(this CourseVersion version)
    {
        Ensure.That(version).IsNotNull();

        return new CatalogCourseDto(version.CourseId, version.Id, version.VersionNumber, version.Title, version.ValidityMonths);
    }

    public static CatalogCourseVersionDto ToCatalogCourseVersionDto(this CourseVersion version, CatalogCourseVersionStatus status)
    {
        Ensure.That(version).IsNotNull();

        return new CatalogCourseVersionDto(
            version.CourseId,
            version.Id,
            version.VersionNumber,
            version.Title,
            version.ValidityMonths,
            status == CatalogCourseVersionStatus.Retired,
            status,
            [.. version.Chapters.OrderBy(chapter => chapter.Position).ThenBy(chapter => chapter.ChapterId).Select(ToCatalogChapterDto)]);
    }

    private static CatalogChapterDto ToCatalogChapterDto(CourseChapter chapter) =>
        new(chapter.ChapterId, chapter.Position, chapter.Title, ToCatalogChapterKind(chapter.Kind), chapter.VideoDurationSeconds, chapter.Text);

    private static CatalogChapterKind ToCatalogChapterKind(ChapterKind kind) => kind switch
    {
        ChapterKind.Text => CatalogChapterKind.Text,
        ChapterKind.Pdf => CatalogChapterKind.Pdf,
        ChapterKind.Video => CatalogChapterKind.Video,
        _ => throw new InvalidOperationException($"Chapter kind {kind} has no counterpart in the catalog's answers."),
    };
}
