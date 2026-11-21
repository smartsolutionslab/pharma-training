using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Catalog.Domain.Samples;

namespace SmartSolutionsLab.PharmaTraining.Modules.Catalog.Application.Commands.CreateSample;

public sealed class CreateSampleHandler(ISampleStore samples, ICatalogUnitOfWork unitOfWork)
    : ICommandHandler<CreateSample, Result<Guid, ApiError>>
{
    public async Task<Result<Guid, ApiError>> HandleAsync(CreateSample command, CancellationToken cancellationToken)
    {
        Ensure.That(command).IsNotNull();

        if (string.IsNullOrWhiteSpace(command.Name) || command.Name.Length > Sample.MaxNameLength)
        {
            return Failure(SampleFailures.InvalidName());
        }

        var sample = new Sample { Id = Guid.CreateVersion7(), Name = command.Name };
        samples.Add(sample);
        await unitOfWork.CommitAsync(cancellationToken);
        return Success(sample.Id);
    }
}
