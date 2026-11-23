using System.Collections;
using System.Reflection;
using SmartSolutionsLab.PharmaTraining.Contracts.Assessment;

namespace SmartSolutionsLab.PharmaTraining.Architecture.Tests.Rules;

/// <summary>
/// The solutions go to Assessment and never to the frontend (docs/domain/README.md): nothing Assessment answers or
/// publishes carries a member that names correctness, or the types that hold the solutions, however deep.
/// </summary>
public sealed class AssessmentResponsesCarryNoSolutions : IArchitectureRule
{
    private const string Api = "SmartSolutionsLab.PharmaTraining.Modules.Assessment.Api";
    private const string Application = "SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application";
    private const string Contracts = "SmartSolutionsLab.PharmaTraining.Contracts.Assessment";

    /// <summary>The types that hold the solutions: for scoring only, never on a response.</summary>
    private static readonly HashSet<string> SolutionTypes =
    [
        "SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations.TestDefinition",
        "SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations.DefinedQuestion",
        "SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.ReadModels.TestDefinitionCopy",
        "SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.ReadModels.TestQuestionCopy",
        "SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.ReadModels.TestOptionCopy",
    ];

    public string Description =>
        $"No type of Assessment's Api, no *Dto of its Application and no event in {Contracts} reaches, through its public properties, collections and generic arguments, a property whose name contains \"Correct\" or a type that holds the solutions ({string.Join(", ", SolutionTypes.Select(type => type[(type.LastIndexOf('.') + 1)..]))}).";

    /// <summary>The violations reachable from each of <paramref name="types"/>, each named with its path from the root.</summary>
    public static IEnumerable<string> Violations(IEnumerable<Type> types)
    {
        Ensure.That(types).IsNotNull();

        return types.SelectMany(root => Walk(root, root.Name, [root]));
    }

    public IEnumerable<string> Violations(ArchitectureModel model)
    {
        Ensure.That(model).IsNotNull();

        var api = model.ModuleAssemblies.Where(assembly => assembly.GetName().Name == Api).SelectMany(assembly => assembly.GetTypes());
        var dtos = model.ModuleAssemblies
            .Where(assembly => assembly.GetName().Name == Application)
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type.Name.EndsWith("Dto", StringComparison.Ordinal));
        var published = typeof(TestPassedV1).Assembly.GetTypes().Where(type => type.Namespace == Contracts);
        return Violations(api.Concat(dtos).Concat(published).Where(type => !IsCompilerGenerated(type)));
    }

    private static IEnumerable<string> Walk(Type type, string path, HashSet<Type> onPath)
    {
        if (type.FullName is { } name && SolutionTypes.Contains(name))
        {
            yield return $"{path} is a {type.Name}, which holds the solutions; only Assessment's scoring may read it.";
            yield break;
        }

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(property => property.GetIndexParameters().Length == 0))
        {
            var propertyPath = $"{path}.{property.Name}";
            if (property.Name.Contains("Correct", StringComparison.OrdinalIgnoreCase))
            {
                yield return $"{propertyPath} names correctness; a learner never sees which answer is right.";
                continue;
            }

            foreach (var (reached, reachedPath) in Reached(property.PropertyType, propertyPath))
            {
                if (IsOwn(reached) && onPath.Add(reached))
                {
                    foreach (var violation in Walk(reached, reachedPath, onPath))
                    {
                        yield return violation;
                    }

                    onPath.Remove(reached);
                }
            }
        }
    }

    /// <summary>The types a member of this type leads to: itself, a collection's elements (<c>[]</c>) and its generic arguments.</summary>
    private static IEnumerable<(Type Type, string Path)> Reached(Type type, string path)
    {
        if (Nullable.GetUnderlyingType(type) is { } underlying)
        {
            return Reached(underlying, path);
        }

        if (type.IsArray)
        {
            return Reached(type.GetElementType()!, $"{path}[]");
        }

        if (type != typeof(string) && typeof(IEnumerable).IsAssignableFrom(type) && ElementType(type) is { } element)
        {
            return Reached(element, $"{path}[]");
        }

        IEnumerable<(Type, string)> self = [(type, path)];
        return type.IsGenericType
            ? self.Concat(type.GetGenericArguments().SelectMany(argument => Reached(argument, path)))
            : self;
    }

    private static Type? ElementType(Type type) =>
        (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>) ? type : null)
            ?.GetGenericArguments()[0]
        ?? type.GetInterfaces()
            .FirstOrDefault(contract => contract.IsGenericType && contract.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            ?.GetGenericArguments()[0];

    /// <summary>Only the project's own types are walked into; a BCL or framework type holds no solution of ours.</summary>
    private static bool IsOwn(Type type) =>
        !type.IsGenericParameter
        && type.Namespace is { } ns
        && ns.StartsWith("SmartSolutionsLab.PharmaTraining.", StringComparison.Ordinal);

    private static bool IsCompilerGenerated(Type type) =>
        type.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), inherit: false);
}
