using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Storage;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.AddFileChapter;

/// <summary>Red-test stub for #15 (task A3): answers <see cref="NotYetImplemented"/>. B5 implements it.</summary>
public sealed class AddFileChapterHandler : ICommandHandler<AddFileChapter, Result<Guid, ApiError>>
{
    public AddFileChapterHandler(ICourseVersionStore versions, IEditorialUnitOfWork unitOfWork, IStoredBlobs blobs)
    {
        Ensure.That(versions).IsNotNull();
        Ensure.That(unitOfWork).IsNotNull();
        Ensure.That(blobs).IsNotNull();
    }

    public Task<Result<Guid, ApiError>> HandleAsync(AddFileChapter command, CancellationToken cancellationToken)
    {
        Ensure.That(command).IsNotNull();
        return Task.FromResult<Result<Guid, ApiError>>(Failure<ApiError>(new NotYetImplemented()));
    }
}
