using System.Reflection;

namespace SmartSolutionsLab.PharmaTraining.Architecture.Tests.Rules;

/// <summary>
/// Every integration event lives in the contract namespace of a module that exists, so exactly one service may publish it:
/// the broker grants each service the exchanges of its own module's namespace alone (#183). An event in Contracts' root
/// namespace, a sub-namespace or a folder named after no module could be published by no one.
/// </summary>
public sealed class IntegrationEventsBelongToAModule : IArchitectureRule
{
    private const string ModulePrefix = "SmartSolutionsLab.PharmaTraining.Modules.";

    private const string ContractsPrefix = "SmartSolutionsLab.PharmaTraining.Contracts.";

    private const string IntegrationEvent = "SmartSolutionsLab.PharmaTraining.Contracts.IntegrationEvent";

    public string Description => "Every integration event lives in SmartSolutionsLab.PharmaTraining.Contracts.<Module> for a module that exists, so the one service the broker lets publish it is its module's (#183).";

    /// <summary>The integration events among the types that are not in the contract namespace of one of the modules.</summary>
    public static IEnumerable<string> Violations(IEnumerable<Type> types, IEnumerable<string> modules)
    {
        Ensure.That(types).IsNotNull();
        Ensure.That(modules).IsNotNull();

        var known = modules.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var type in types.Where(IsIntegrationEvent).OrderBy(type => type.FullName, StringComparer.Ordinal))
        {
            var @namespace = type.Namespace ?? string.Empty;
            var module = @namespace.StartsWith(ContractsPrefix, StringComparison.Ordinal) ? @namespace[ContractsPrefix.Length..] : null;
            if (module is null || module.Contains('.', StringComparison.Ordinal) || !known.Contains(module))
            {
                yield return $"{type.FullName} is not in {ContractsPrefix}<Module> for a module that exists ({string.Join(", ", known.Order(StringComparer.Ordinal))}); no service may publish it.";
            }
        }
    }

    public IEnumerable<string> Violations(ArchitectureModel model)
    {
        Ensure.That(model).IsNotNull();

        var modules = model.ModuleAssemblies
            .Select(assembly => assembly.GetName().Name!)
            .Where(name => name.StartsWith(ModulePrefix, StringComparison.Ordinal))
            .Select(name => name[ModulePrefix.Length..].Split('.')[0])
            .Distinct(StringComparer.Ordinal);

        return Violations(Assembly.Load("Contracts").GetTypes(), modules);
    }

    private static bool IsIntegrationEvent(Type type)
    {
        if (type is not { IsClass: true, IsAbstract: false })
        {
            return false;
        }

        for (var baseType = type.BaseType; baseType is not null; baseType = baseType.BaseType)
        {
            if (baseType.FullName == IntegrationEvent)
            {
                return true;
            }
        }

        return false;
    }
}
