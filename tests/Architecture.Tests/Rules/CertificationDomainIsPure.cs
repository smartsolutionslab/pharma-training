namespace SmartSolutionsLab.PharmaTraining.Architecture.Tests.Rules;

/// <summary>Per module: the rule checks only the modules that chose ddd, so another module may choose another style.</summary>
public sealed class CertificationDomainIsPure : IArchitectureRule
{
    private const string Domain = "SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain";

    public string Description => "The Certification module is domain-driven: its Domain project references nothing but the BCL, Kernel and BuildingBlocks.Domain.";

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
        name is "netstandard" or "mscorlib" or "System" or "SmartSolutionsLab.PharmaTraining.Kernel" or "SmartSolutionsLab.PharmaTraining.BuildingBlocks.Domain"
        || name.StartsWith("System.", StringComparison.Ordinal);
}
