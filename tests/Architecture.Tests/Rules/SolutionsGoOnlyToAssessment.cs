using System.Reflection;
using SmartSolutionsLab.PharmaTraining.Contracts.Editorial;

namespace SmartSolutionsLab.PharmaTraining.Architecture.Tests.Rules;

public sealed class SolutionsGoOnlyToAssessment : IArchitectureRule
{
    public const string AllowedAssembly = "SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application";

    public string Description => $"Only Assessment's Application ({AllowedAssembly}, matched by its exact name) may have a *Consumer that takes {nameof(TestDefinitionPublishedV1)}, which carries every question's solution (docs/domain/README.md). It checks Wolverine consumers only: what an endpoint answers is not checked here.";

    public static IEnumerable<string> Violations(IEnumerable<Type> types, string allowedAssembly)
    {
        Ensure.That(types).IsNotNull();
        Ensure.That(allowedAssembly).IsNotNullOrWhiteSpace();

        foreach (var type in types.Where(type => type.IsClass
            && type.Name.EndsWith("Consumer", StringComparison.Ordinal)
            && !string.Equals(type.Assembly.GetName().Name, allowedAssembly, StringComparison.Ordinal)))
        {
            var consumes = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.FlattenHierarchy)
                .Any(method => method.GetParameters().Any(parameter => parameter.ParameterType == typeof(TestDefinitionPublishedV1)));
            if (consumes)
            {
                yield return $"{type.FullName} in {type.Assembly.GetName().Name} takes {nameof(TestDefinitionPublishedV1)}; only Assessment may receive the questions' solutions.";
            }
        }
    }

    public IEnumerable<string> Violations(ArchitectureModel model)
    {
        Ensure.That(model).IsNotNull();

        return Violations(model.ModuleAssemblies.SelectMany(assembly => assembly.GetTypes()), AllowedAssembly);
    }
}
