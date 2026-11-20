using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.Samples;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.Commands.CreateSample;

/// <summary>Stores the new sample's events, then writes its row of the samples' table, which the queries read.</summary>
public sealed class CreateSampleHandler(ILearningAggregateStore samples, ISampleSummaries summaries, ILearningUnitOfWork unitOfWork)
    : ICommandHandler<CreateSample, Result<Guid, ApiError>>
{
    public async Task<Result<Guid, ApiError>> HandleAsync(CreateSample command, CancellationToken cancellationToken)
    {
        Ensure.That(command).IsNotNull();

        var sample = Sample.Create(SampleId.New(), command.Name);
        await samples.SaveAsync(sample, cancellationToken);
        await summaries.RecordAsync(sample, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        return Success(sample.Id.Value);
    }
}
