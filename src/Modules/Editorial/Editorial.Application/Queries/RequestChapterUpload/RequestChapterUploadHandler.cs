using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Storage;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.DTOs;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Queries.RequestChapterUpload;

/// <summary>
/// Signs an upload URL, for fifteen minutes, under a new name for a draft's PDF or video. Nothing is stored: a version
/// published in the meantime refuses the chapter that names the upload.
/// </summary>
public sealed class RequestChapterUploadHandler(ICourseVersionStore versions, IBlobUrls urls)
    : IQueryHandler<RequestChapterUpload, Result<EditorialChapterUploadDto, ApiError>>
{
    public async Task<Result<EditorialChapterUploadDto, ApiError>> HandleAsync(RequestChapterUpload query, CancellationToken cancellationToken)
    {
        Ensure.That(query).IsNotNull();

        if (query.Kind is not (EditorialChapterKind.Pdf or EditorialChapterKind.Video))
        {
            return Failure(CourseVersionFailures.InvalidUploadKind());
        }

        if (query.Version == Guid.Empty)
        {
            return Failure(CourseVersionFailures.NotFound(query.Version));
        }

        var version = CourseVersionId.From(query.Version);
        var found = await versions.FindAsync(version, cancellationToken);
        if (!found.HasValue)
        {
            return Failure(CourseVersionFailures.NotFound(query.Version));
        }

        var accepts = found.Value.AcceptsUploads();
        if (accepts.IsFailure)
        {
            return Failure(CourseVersionFailures.From(accepts.Error));
        }

        var kind = query.Kind.ToDomain();
        var name = ChapterUploads.NameFor(version, kind);
        var url = await urls.CreateUploadUrlAsync(name, BlobUrlLifetime.From(BlobUrlLifetime.Maximum), cancellationToken);
        return Success(new ChapterUpload(name, url, ChapterUploads.ContentTypeOf(kind), ChapterUploads.MaxBytesOf(kind)).ToDto());
    }
}
