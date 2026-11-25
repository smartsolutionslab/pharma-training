using System.Reflection;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;

namespace SmartSolutionsLab.PharmaTraining.Architecture.Tests.Rules;

/// <summary>
/// The host dead-letters an <see cref="IPermanentMessageFailure"/> on its first failure, without a retry
/// (src/Host/Wiring/Messaging.cs, ADR 0004), and it matches only the outermost exception. So the marker belongs only on
/// a failure certain to fail the same way on every retry: an exception (nothing else can be thrown), sealed (a subclass
/// could be thrown for a transient cause), and with no constructor that takes an inner exception (the cause it wraps could
/// be a dropped connection or a lost race). Its doc comment said so; this rule holds it (#198's review). It looks at every
/// class and struct (a struct can never be an exception) in every assembly of the project's code the architecture tests
/// load, not only the modules' (#401's review).
/// </summary>
public sealed class PermanentMessageFailuresAreSealedExceptions : IArchitectureRule
{
    public string Description => $"Every {nameof(IPermanentMessageFailure)} is a sealed exception with no constructor that takes an inner exception.";

    public static IEnumerable<string> Violations(IEnumerable<Type> types)
    {
        Ensure.That(types).IsNotNull();

        foreach (var type in types.Where(type => (type.IsClass || type.IsValueType) && typeof(IPermanentMessageFailure).IsAssignableFrom(type)))
        {
            if (!typeof(Exception).IsAssignableFrom(type))
            {
                yield return $"{type.FullName} is an {nameof(IPermanentMessageFailure)} but no exception, so no consumer can throw it; mark only an exception.";
                continue;
            }

            if (!type.IsSealed)
            {
                yield return $"{type.FullName} is an {nameof(IPermanentMessageFailure)} that is not sealed: a subclass could be thrown for a transient cause and still be dead-lettered without a retry.";
            }

            if (type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .Any(constructor => constructor.GetParameters().Any(parameter => typeof(Exception).IsAssignableFrom(parameter.ParameterType))))
            {
                yield return $"{type.FullName} is an {nameof(IPermanentMessageFailure)} with a constructor that takes an inner exception, which may be transient; it must carry none.";
            }
        }
    }

    public IEnumerable<string> Violations(ArchitectureModel model)
    {
        Ensure.That(model).IsNotNull();
        return Violations(model.ProjectAssemblies.SelectMany(assembly => assembly.GetTypes()));
    }
}
