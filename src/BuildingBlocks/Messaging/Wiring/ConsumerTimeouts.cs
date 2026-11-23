using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using Wolverine;

namespace Microsoft.Extensions.DependencyInjection;

public static class ConsumerTimeouts
{
    /// <summary>
    /// Applies <c>[ConsumerTimeout]</c>: a consumer that carries it is cancelled after its own timeout instead of
    /// Wolverine's default of 60 seconds, which stays the default for every other one. Registered once however often it is called.
    /// </summary>
    public static WolverineOptions UseConsumerTimeouts(this WolverineOptions options)
    {
        Ensure.That(options).IsNotNull();

        if (!options.Policies.OfType<ConsumerTimeoutPolicy>().Any())
        {
            options.Policies.Add<ConsumerTimeoutPolicy>();
        }

        return options;
    }
}
