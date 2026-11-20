namespace SmartSolutionsLab.PharmaTraining.Architecture.Tests.Rules;

public sealed class CertificationHoldsNoPrimitives : IArchitectureRule
{
    private const string Module = "SmartSolutionsLab.PharmaTraining.Modules.Certification.";
    private const string AggregateRoot = "SmartSolutionsLab.PharmaTraining.BuildingBlocks.Domain.AggregateRoot`1";
    private const string DomainEvent = "SmartSolutionsLab.PharmaTraining.BuildingBlocks.Domain.IDomainEvent";

    public string Description =>
        "The Certification module is strict: no aggregate, entity or value object reachable from one, command or domain event holds a primitive; a value object holds its own backing values.";

    public IEnumerable<string> Violations(ArchitectureModel model)
    {
        Ensure.That(model).IsNotNull();

        return PrimitiveBoundary.Violations(
            [.. model.ModuleAssemblies.Where(assembly => assembly.GetName().Name!.StartsWith(Module, StringComparison.Ordinal))],
            AggregateRoot,
            DomainEvent);
    }
}
