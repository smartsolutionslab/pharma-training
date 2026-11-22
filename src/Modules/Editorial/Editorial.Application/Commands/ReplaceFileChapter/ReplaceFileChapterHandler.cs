using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Storage;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.ReplaceFileChapter;

/// <summary>Red-test stub for #15 (task A3): answers <see cref="NotYetImplemented"/>. B5 implements it.</summary>
public sealed class ReplaceFileChapterHandler : ICommandHandler<ReplaceFileChapter, Result<Unit, ApiError>>
{
    public ReplaceFileChapterHandler(ICourseVersionStore versions, IEditorialUnitOfWork unitOfWork, IStoredBlobs blobs)
    {
        Ensure.That(versions).IsNotNull();
        Ensure.That(unitOfWork).IsNotNull();
        Ensure.That(blobs).IsNotNull();
    }

    public Task<Result<Unit, ApiError>> HandleAsync(ReplaceFileChapter command, CancellationToken cancellationToken)
    {
        Ensure.That(command).IsNotNull();
        return Task.FromResult<Result<Unit, ApiError>>(Failure<ApiError>(new NotYetImplemented()));
    }
}
