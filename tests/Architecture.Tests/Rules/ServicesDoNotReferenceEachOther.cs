namespace SmartSolutionsLab.PharmaTraining.Architecture.Tests.Rules;

public sealed class ServicesDoNotReferenceEachOther : IArchitectureRule
{
    public string Description => "No service host references another module's projects.";

    public IEnumerable<string> Violations(ArchitectureModel model)
    {
        Ensure.That(model).IsNotNull();

        return [.. ServiceHostReferences.Read()
            .SelectMany(service => service.Value
                .Where(reference => ServiceHostReferences.ModuleOf(reference) != service.Key)
                .Select(reference => $"src/Services/{service.Key}/Host/Host.csproj references {reference}, a project of the module {ServiceHostReferences.ModuleOf(reference)}."))];
    }
}
