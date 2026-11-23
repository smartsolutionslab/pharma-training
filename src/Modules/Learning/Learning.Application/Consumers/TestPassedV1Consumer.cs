using SmartSolutionsLab.PharmaTraining.Contracts.Assessment;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.Consumers;

/// <summary>Stub for #42's red run: takes Assessment's passed test off Learning's queue and does nothing with it yet.</summary>
public sealed class TestPassedV1Consumer(ILearningAggregateStore sessions)
{
    public Task HandleAsync(TestPassedV1 message, CancellationToken cancellationToken)
    {
        _ = (sessions, message, cancellationToken);
        return Task.CompletedTask;
    }
}
