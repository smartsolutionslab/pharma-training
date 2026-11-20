namespace SmartSolutionsLab.PharmaTraining.Architecture.Tests.Rules;

public sealed class OnlyInfrastructureUsesMarten : IArchitectureRule
{
    public string Description => "Only a module's Infrastructure project references Marten.";

    public IEnumerable<string> Violations(ArchitectureModel model)
    {
        Ensure.That(model).IsNotNull();

        foreach (var assembly in model.ModuleAssemblies.Where(assembly => !assembly.GetName().Name!.EndsWith(".Infrastructure", StringComparison.Ordinal)))
        {
            if (assembly.GetReferencedAssemblies().Any(reference => reference.Name is "Marten" || reference.Name!.StartsWith("Marten.", StringComparison.Ordinal)))
            {
                yield return $"{assembly.GetName().Name} references Marten; only a module's Infrastructure talks to the event store, the rest uses I<Module>AggregateStore.";
            }
        }
    }
}
