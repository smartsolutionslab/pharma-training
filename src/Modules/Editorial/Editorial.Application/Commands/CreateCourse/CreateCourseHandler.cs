using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.CreateCourse;

public sealed class CreateCourseHandler(ICourseVersionStore versions, IEditorialUnitOfWork unitOfWork)
    : ICommandHandler<CreateCourse, Result<Guid, ApiError>>
{
    public async Task<Result<Guid, ApiError>> HandleAsync(CreateCourse command, CancellationToken cancellationToken)
    {
        Ensure.That(command).IsNotNull();

        var version = CourseVersion.DraftFirst(CourseVersionId.New(), CourseId.New(), command.Title, command.Editor);
        versions.Add(version);
        await unitOfWork.CommitAsync(cancellationToken);
        return Success(version.Id.Value);
    }
}
