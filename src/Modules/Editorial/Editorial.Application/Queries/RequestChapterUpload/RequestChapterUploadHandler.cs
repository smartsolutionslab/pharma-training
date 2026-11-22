using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Storage;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.DTOs;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Queries.RequestChapterUpload;

/// <summary>Red-test stub for #15 (task A3): answers <see cref="NotYetImplemented"/>. B4 implements it.</summary>
public sealed class RequestChapterUploadHandler : IQueryHandler<RequestChapterUpload, Result<EditorialChapterUploadDto, ApiError>>
{
    public RequestChapterUploadHandler(ICourseVersionStore versions, IBlobUrls urls)
    {
        Ensure.That(versions).IsNotNull();
        Ensure.That(urls).IsNotNull();
    }

    public Task<Result<EditorialChapterUploadDto, ApiError>> HandleAsync(RequestChapterUpload query, CancellationToken cancellationToken)
    {
        Ensure.That(query).IsNotNull();
        return Task.FromResult<Result<EditorialChapterUploadDto, ApiError>>(Failure<ApiError>(new NotYetImplemented()));
    }
}
