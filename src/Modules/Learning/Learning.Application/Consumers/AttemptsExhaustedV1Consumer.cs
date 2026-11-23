using SmartSolutionsLab.PharmaTraining.Contracts.Assessment;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.Consumers;

/// <summary>Stub for #42's red run: takes Assessment's exhaustion off Learning's queue and does nothing with it yet.</summary>
public sealed class AttemptsExhaustedV1Consumer(ILearningAggregateStore sessions, TimeProvider time)
{
    public Task HandleAsync(AttemptsExhaustedV1 message, CancellationToken cancellationToken)
    {
        _ = (sessions, time, message, cancellationToken);
        return Task.CompletedTask;
    }
}
