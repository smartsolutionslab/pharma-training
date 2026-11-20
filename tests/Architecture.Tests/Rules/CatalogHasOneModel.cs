namespace SmartSolutionsLab.PharmaTraining.Architecture.Tests.Rules;

public sealed class CatalogHasOneModel : IArchitectureRule
{
    private const string Module = "SmartSolutionsLab.PharmaTraining.Modules.Catalog.";

    public string Description => "The Catalog module has no CQRS: no separate read models or projections; it reads the entities it writes.";

    public IEnumerable<string> Violations(ArchitectureModel model)
    {
        Ensure.That(model).IsNotNull();

        var readSide = model.ModuleAssemblies
            .Where(assembly => assembly.GetName().Name!.StartsWith(Module, StringComparison.Ordinal))
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => !type.IsNested && (type.Name.EndsWith("ReadModel", StringComparison.Ordinal) || type.Name.EndsWith("Projection", StringComparison.Ordinal)));

        foreach (var type in readSide)
        {
            yield return $"{type.FullName} is a separate read model; without CQRS a module reads the entities it writes.";
        }
    }
}
