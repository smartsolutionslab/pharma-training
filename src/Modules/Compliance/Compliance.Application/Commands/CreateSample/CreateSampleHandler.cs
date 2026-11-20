using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Samples;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.Commands.CreateSample;

public sealed class CreateSampleHandler(ISampleStore samples, IComplianceUnitOfWork unitOfWork)
    : ICommandHandler<CreateSample, Result<Guid, ApiError>>
{
    public async Task<Result<Guid, ApiError>> HandleAsync(CreateSample command, CancellationToken cancellationToken)
    {
        Ensure.That(command).IsNotNull();

        var sample = Sample.Create(SampleId.New(), command.Name);
        samples.Add(sample);
        await unitOfWork.CommitAsync(cancellationToken);
        return Success(sample.Id.Value);
    }
}
