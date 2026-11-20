namespace SmartSolutionsLab.PharmaTraining.Architecture.Tests.Rules;

/// <summary>Per module: the rule checks only the modules that chose event-sourced, so another module may choose another style.</summary>
public sealed class LearningEventSourcedDomainIsPure : IArchitectureRule
{
    private const string Domain = "SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain";

    public string Description => "The Learning module is event-sourced: its Domain project references nothing but the BCL, Kernel and BuildingBlocks.EventSourcing.";

    public IEnumerable<string> Violations(ArchitectureModel model)
    {
        Ensure.That(model).IsNotNull();

        foreach (var reference in model.ModuleAssemblies
            .Where(assembly => assembly.GetName().Name == Domain)
            .SelectMany(assembly => assembly.GetReferencedAssemblies())
            .Select(reference => reference.Name!)
            .Where(name => !IsAllowed(name))
            .Order(StringComparer.Ordinal))
        {
            yield return $"{Domain} references {reference}.";
        }
    }

    private static bool IsAllowed(string name) =>
        name is "netstandard" or "mscorlib" or "System" or "SmartSolutionsLab.PharmaTraining.Kernel" or "SmartSolutionsLab.PharmaTraining.BuildingBlocks.EventSourcing"
        || name.StartsWith("System.", StringComparison.Ordinal);
}
