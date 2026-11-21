using System.Reflection;
using System.Text.RegularExpressions;

namespace SmartSolutionsLab.PharmaTraining.Architecture.Tests.Rules;

public sealed partial class EventSourcedEventsCarryNoPersonalData : IArchitectureRule
{
    private static readonly string[] EventSourcedNamespaces =
    [
        "SmartSolutionsLab.PharmaTraining.Contracts.Learning",
        "SmartSolutionsLab.PharmaTraining.Contracts.Assessment",
    ];

    public string Description => "Integration events of Learning and Assessment carry no name or e-mail at any depth, only PersonId, so a person can be erased from the services that keep their names (docs/domain/README.md). Their internal domain events are not checked here.";

    public static IReadOnlyList<Type> EventsOfEventSourcedModules() =>
        [.. Assembly.Load("Contracts")
            .GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false }
                && EventSourcedNamespaces.Contains(type.Namespace)
                && type.GetInterfaces().Any(contract => contract.FullName == "SmartSolutionsLab.PharmaTraining.Contracts.IIntegrationEvent"))];

    public static IEnumerable<string> Violations(IEnumerable<Type> events)
    {
        Ensure.That(events).IsNotNull();

        foreach (var type in events)
        {
            HashSet<Type> visited = [];
            foreach (var violation in Walk(type, type.FullName ?? type.Name, visited))
            {
                yield return violation;
            }
        }
    }

    public IEnumerable<string> Violations(ArchitectureModel model)
    {
        Ensure.That(model).IsNotNull();

        return Violations(EventsOfEventSourcedModules());
    }

    private static IEnumerable<string> Walk(Type type, string path, HashSet<Type> visited)
    {
        if (!visited.Add(type))
        {
            yield break;
        }

        const BindingFlags Public = BindingFlags.Public | BindingFlags.Instance;
        var members = type.GetProperties(Public).Select(property => (property.Name, Type: property.PropertyType))
            .Concat(type.GetFields(Public).Select(field => (field.Name, Type: field.FieldType)));

        foreach (var (name, memberType) in members)
        {
            if (PersonalData().IsMatch(name))
            {
                yield return $"{path}.{name} looks like personal data; events of Learning and Assessment carry only PersonId (docs/domain/README.md).";
            }

            var walked = ElementType(memberType);
            if (IsWalked(walked))
            {
                foreach (var violation in Walk(walked, $"{path}.{name}", visited))
                {
                    yield return violation;
                }
            }
        }
    }

    private static Type ElementType(Type type)
    {
        if (type == typeof(string))
        {
            return type;
        }

        if (Nullable.GetUnderlyingType(type) is { } underlying)
        {
            return ElementType(underlying);
        }

        var enumerable = type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>)
            ? type
            : type.GetInterfaces().FirstOrDefault(contract => contract.IsGenericType && contract.GetGenericTypeDefinition() == typeof(IEnumerable<>));

        return enumerable is null ? type : ElementType(enumerable.GetGenericArguments()[0]);
    }

    private static bool IsWalked(Type type) =>
        (type.IsClass || type.IsValueType) && !type.IsEnum && !type.IsPrimitive && type.Namespace?.StartsWith("System", StringComparison.Ordinal) != true;

    [GeneratedRegex("name|mail", RegexOptions.IgnoreCase)]
    private static partial Regex PersonalData();
}
