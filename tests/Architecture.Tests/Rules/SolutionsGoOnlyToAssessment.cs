using System.Reflection;
using SmartSolutionsLab.PharmaTraining.Contracts.Editorial;

namespace SmartSolutionsLab.PharmaTraining.Architecture.Tests.Rules;

public sealed class SolutionsGoOnlyToAssessment : IArchitectureRule
{
    private const string AssessmentAssemblies = "Assessment.";

    public string Description => $"Only Assessment consumes {nameof(TestDefinitionPublishedV1)}, which carries every question's solution; no other module and never the frontend (docs/domain/README.md).";

    public static IEnumerable<string> Violations(IEnumerable<Type> types, string allowedAssemblyPrefix)
    {
        Ensure.That(types).IsNotNull();
        Ensure.That(allowedAssemblyPrefix).IsNotNullOrWhiteSpace();

        foreach (var type in types.Where(type => type.IsClass
            && type.Name.EndsWith("Consumer", StringComparison.Ordinal)
            && type.Assembly.GetName().Name?.StartsWith(allowedAssemblyPrefix, StringComparison.Ordinal) != true))
        {
            var consumes = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
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

        return Violations(model.ModuleAssemblies.SelectMany(assembly => assembly.GetTypes()), AssessmentAssemblies);
    }
}
