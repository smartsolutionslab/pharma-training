using System.Reflection;
using JasperFx;
using JasperFx.CodeGeneration;
using Wolverine.Configuration;
using Wolverine.Runtime.Handlers;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;

/// <summary>
/// Gives each handler chain whose consumer carries <see cref="ConsumerTimeoutAttribute"/> (on its handler method, or else
/// on its class) that timeout, Wolverine's <see cref="HandlerChain.ExecutionTimeoutInSeconds"/>; a chain of several
/// handlers gets the longest. Every other chain keeps Wolverine's <c>DefaultExecutionTimeout</c>, which stays as it is.
/// </summary>
public sealed class ConsumerTimeoutPolicy : IHandlerPolicy
{
    public void Apply(IReadOnlyList<HandlerChain> chains, GenerationRules rules, IServiceContainer container)
    {
        Ensure.That(chains).IsNotNull();

        foreach (var chain in chains)
        {
            var timeouts = chain.Handlers
                .Select(handler => handler.Method.GetCustomAttribute<ConsumerTimeoutAttribute>()
                    ?? handler.HandlerType.GetCustomAttribute<ConsumerTimeoutAttribute>())
                .OfType<ConsumerTimeoutAttribute>()
                .Select(timeout => (int)timeout.Timeout.TotalSeconds)
                .ToList();
            if (timeouts.Count > 0)
            {
                chain.ExecutionTimeoutInSeconds = timeouts.Max();
            }
        }
    }
}
