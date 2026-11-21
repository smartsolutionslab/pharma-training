using System.Reflection;

namespace SmartSolutionsLab.PharmaTraining.Architecture.Tests.Rules;

/// <summary>
/// Light's rule, per strict module: strict is light and more, so a project whose modules are all strict keeps it.
/// </summary>
public sealed class ComplianceAggregatesHaveNoPublicSetters : IArchitectureRule
{
    private const string Module = "SmartSolutionsLab.PharmaTraining.Modules.Compliance.";

    public string Description => "No aggregate root of the Compliance module exposes a public setter.";

    public IEnumerable<string> Violations(ArchitectureModel model)
    {
        Ensure.That(model).IsNotNull();

        return model.ModuleAssemblies
            .Where(assembly => assembly.GetName().Name!.StartsWith(Module, StringComparison.Ordinal))
            .SelectMany(assembly => assembly.GetTypes())
            .Where(IsAggregateRoot)
            .SelectMany(type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(property => property.SetMethod is { IsPublic: true })
                .Select(property => $"{type.FullName}.{property.Name} has a public setter."));
    }

    private static bool IsAggregateRoot(Type type)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current.IsGenericType && current.GetGenericTypeDefinition().FullName == "SmartSolutionsLab.PharmaTraining.BuildingBlocks.Domain.AggregateRoot`1")
            {
                return true;
            }
        }

        return false;
    }
}
