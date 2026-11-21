namespace SmartSolutionsLab.PharmaTraining.Architecture.Tests.Rules;

/// <summary>
/// The Assessment module is strict and event-sourced: its event store writes a value object as its one backing
/// value, so a value object over several values (<c>Money</c>) is refused for now (module.ddd-depth: strict, docs/adr).
/// </summary>
public sealed class AssessmentValueObjectsHaveOneValue : IArchitectureRule
{
    private const string Domain = "SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain";
    private const string Aggregate = "SmartSolutionsLab.PharmaTraining.BuildingBlocks.EventSourcing.EventSourcedAggregate`1";
    private const string DomainEvent = "SmartSolutionsLab.PharmaTraining.BuildingBlocks.EventSourcing.IDomainEvent";

    public string Description =>
        "The Assessment module is strict and event-sourced: every value object of its Domain is an IValueObject<T> over exactly one value.";

    public IEnumerable<string> Violations(ArchitectureModel model)
    {
        Ensure.That(model).IsNotNull();

        return model.ModuleAssemblies
            .Where(assembly => assembly.GetName().Name == Domain)
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => !type.IsAbstract && !type.IsInterface && typeof(IValueObject).IsAssignableFrom(type) && !IsAggregate(type) && !IsDomainEvent(type))
            .Where(type => type.GetInterfaces().Count(contract => contract.IsGenericType && contract.GetGenericTypeDefinition() == typeof(IValueObject<>)) != 1)
            .Select(type => $"{type.FullName} is a value object over more than one value; the event store writes a value object as its one backing value, so implement IValueObject<T> for exactly one T.");
    }

    private static bool IsDomainEvent(Type type) => type.GetInterfaces().Any(contract => contract.FullName == DomainEvent);

    private static bool IsAggregate(Type type)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current.IsGenericType && current.GetGenericTypeDefinition().FullName == Aggregate)
            {
                return true;
            }
        }

        return false;
    }
}
