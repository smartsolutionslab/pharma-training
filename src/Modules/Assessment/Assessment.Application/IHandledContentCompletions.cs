using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.ReadModels;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application;

/// <summary>The <c>ContentCompletedV1</c> events Assessment handled, by event identifier; implemented in its Infrastructure project.</summary>
public interface IHandledContentCompletions
{
    Task<bool> ContainsAsync(Guid eventIdentifier, CancellationToken cancellationToken);

    /// <summary>Records the event as handled; committing the module's unit of work stores it.</summary>
    void Add(HandledContentCompletion completion);
}
