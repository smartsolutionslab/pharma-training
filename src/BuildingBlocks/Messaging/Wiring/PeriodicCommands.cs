using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;

namespace Microsoft.Extensions.DependencyInjection;

public static class PeriodicCommands
{
    /// <summary>Not implemented yet (#115, slice 5): registers nothing.</summary>
    public static IServiceCollection AddPeriodicCommand<TCommand>(this IServiceCollection services, TimeSpan interval)
        where TCommand : ICommand<Result<Unit, ApiError>>, new()
    {
        Ensure.That(services).IsNotNull();
        return services;
    }
}
