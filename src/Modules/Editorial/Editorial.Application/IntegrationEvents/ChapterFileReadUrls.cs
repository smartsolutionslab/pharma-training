using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Storage;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.IntegrationEvents;

/// <summary>
/// Signs the read URLs a <c>ChapterFilesReleasedV1</c> carries (#41, docs/adr/0002), for the publish and for the
/// republish (#264, ADR 0019) alike: a URL is a bearer credential that lives fifteen minutes, so each release signs its
/// own when it is sent, and none is ever stored.
/// </summary>
public static class ChapterFileReadUrls
{
    /// <summary>A read URL on each PDF and video chapter's file, for as long as a URL may live, shown inline with the kind's type; none for a text-only version.</summary>
    public static async Task<Dictionary<ChapterId, BlobUrl>> SignReadUrlsAsync(this IBlobUrls urls, CourseVersion version, CancellationToken cancellationToken)
    {
        Ensure.That(urls).IsNotNull();
        Ensure.That(version).IsNotNull();

        var lifetime = BlobUrlLifetime.From(BlobUrlLifetime.Maximum);
        Dictionary<ChapterId, BlobUrl> readUrls = [];
        foreach (var chapter in version.Chapters)
        {
            // A file chapter's file is a name the module issued, so it is a blob name.
            if (chapter.Kind is ChapterKind.Pdf or ChapterKind.Video && chapter.File is { } file)
            {
                readUrls[chapter.Id] = await urls.CreateReadUrlAsync(BlobName.From(file.Value), lifetime, ChapterUploads.ReadHeadersFor(chapter.Kind), cancellationToken);
            }
        }

        return readUrls;
    }
}
