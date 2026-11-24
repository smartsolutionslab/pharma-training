using System.Collections;
using System.Reflection;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Contracts.Assessment;

namespace SmartSolutionsLab.PharmaTraining.Architecture.Tests.Rules;

/// <summary>
/// The solutions go to Assessment and never to the frontend (docs/domain/README.md): nothing Assessment answers or
/// publishes carries a member that names correctness, a solution or an answer key, or the types that hold the
/// solutions, however deep. A query or command is walked through its result, which is what its caller is answered.
/// </summary>
public sealed class AssessmentResponsesCarryNoSolutions : IArchitectureRule
{
    private const string Api = "SmartSolutionsLab.PharmaTraining.Modules.Assessment.Api";
    private const string Application = "SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application";
    private const string Contracts = "SmartSolutionsLab.PharmaTraining.Contracts.Assessment";

    /// <summary>A public property whose name contains one of these, ignoring case, names a solution.</summary>
    private static readonly string[] SolutionWords = ["Correct", "Solution", "AnswerKey"];

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
        $"No type of Assessment's Api, no *Dto of its Application, no result of a query or command of its Application and no event in {Contracts} reaches, through its public properties, collections and generic arguments, a property whose name contains {string.Join(", ", SolutionWords.Select(word => $"\"{word}\""))} or a type that holds the solutions ({string.Join(", ", SolutionTypes.Select(type => type[(type.LastIndexOf('.') + 1)..]))}).";

    /// <summary>
    /// The violations reachable from each of <paramref name="types"/>, each named with its path from the root. A query or
    /// command is walked through its result (<c>ListX.Result</c>), any other root through its own members.
    /// </summary>
    public static IEnumerable<string> Violations(IEnumerable<Type> types)
    {
        Ensure.That(types).IsNotNull();

        return types.SelectMany(root =>
        {
            var results = root.GetInterfaces().Where(IsRequest).Select(request => request.GetGenericArguments()[0]).ToList();
            return results.Count == 0
                ? Walk(root, root.Name, [root])
                : results.SelectMany(result => Reached(result, $"{root.Name}.Result"))
                    .Where(reached => IsOwn(reached.Type))
                    .SelectMany(reached => Walk(reached.Type, reached.Path, [reached.Type]));
        });
    }

    /// <summary>
    /// Every type of Assessment's Api (its request records included), every *Dto, query and command of its Application,
    /// and every event in <see cref="Contracts"/>; none compiler-generated.
    /// </summary>
    public static IReadOnlyList<Type> Roots(ArchitectureModel model)
    {
        Ensure.That(model).IsNotNull();

        var api = TypesOf(model, Api);
        var application = TypesOf(model, Application).ToList();
        var dtos = application.Where(type => type.Name.EndsWith("Dto", StringComparison.Ordinal));
        var requests = application.Where(type => !type.IsAbstract && type.GetInterfaces().Any(IsRequest));
        var published = typeof(TestPassedV1).Assembly.GetTypes().Where(type => type.Namespace == Contracts);
        return [.. api.Concat(dtos).Concat(requests).Concat(published).Where(type => !IsCompilerGenerated(type)).Distinct()];
    }

    public IEnumerable<string> Violations(ArchitectureModel model) => Violations(Roots(model));

    private static IEnumerable<Type> TypesOf(ArchitectureModel model, string assembly) =>
        model.ModuleAssemblies.Where(candidate => candidate.GetName().Name == assembly).SelectMany(candidate => candidate.GetTypes());

    private static bool IsRequest(Type contract) =>
        contract.IsGenericType
        && !contract.ContainsGenericParameters
        && (contract.GetGenericTypeDefinition() == typeof(IQuery<>) || contract.GetGenericTypeDefinition() == typeof(ICommand<>));

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
            if (SolutionWords.FirstOrDefault(word => property.Name.Contains(word, StringComparison.OrdinalIgnoreCase)) is { } word)
            {
                yield return $"{propertyPath} names a solution ({word}); a learner never sees which answer is right.";
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
