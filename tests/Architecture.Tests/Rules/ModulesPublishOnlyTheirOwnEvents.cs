using System.Reflection;
using System.Reflection.Emit;

namespace SmartSolutionsLab.PharmaTraining.Architecture.Tests.Rules;

/// <summary>
/// A module creates only the integration events of its own contract namespace. The broker lets each service write only to
/// the exchanges of its own module's events (#183), so an event of another module's namespace that a module
/// created would be refused at publish and stay in its outbox.
/// </summary>
public sealed class ModulesPublishOnlyTheirOwnEvents : IArchitectureRule
{
    private const string ModulePrefix = "SmartSolutionsLab.PharmaTraining.Modules.";

    private const string ContractsPrefix = "SmartSolutionsLab.PharmaTraining.Contracts.";

    private const string IntegrationEvent = "SmartSolutionsLab.PharmaTraining.Contracts.IntegrationEvent";

    private const BindingFlags Declared = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    private static readonly Dictionary<short, OpCode> OpCodesByValue = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(field => (OpCode)field.GetValue(null)!)
        .ToDictionary(code => code.Value);

    public string Description => "A module creates only the integration events of its own namespace, SmartSolutionsLab.PharmaTraining.Contracts.<Module>: the broker lets its service publish those alone (#183).";

    /// <summary>
    /// The integration events of another module's contract namespace that the types create, the types taken as the
    /// module's code; the module is matched by name, ignoring case.
    /// </summary>
    public static IEnumerable<string> Violations(string module, IEnumerable<Type> types)
    {
        Ensure.That(module).IsNotNullOrWhiteSpace();
        Ensure.That(types).IsNotNull();

        foreach (var type in types.OrderBy(type => type.FullName, StringComparer.Ordinal))
        {
            var foreign = CreatedEvents(type)
                .Where(created => !string.Equals(created.Namespace, ContractsPrefix + module, StringComparison.OrdinalIgnoreCase))
                .Select(created => created.FullName!)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal);
            foreach (var created in foreign)
            {
                yield return $"{type.FullName} ({module}) creates {created}, an event of another module's contract namespace; its service may publish only {ContractsPrefix}{module}.*, so the broker would refuse it.";
            }
        }
    }

    public IEnumerable<string> Violations(ArchitectureModel model)
    {
        Ensure.That(model).IsNotNull();

        return model.ModuleAssemblies
            .Where(assembly => assembly.GetName().Name!.StartsWith(ModulePrefix, StringComparison.Ordinal))
            .SelectMany(assembly => Violations(ModuleOf(assembly), assembly.GetTypes()));
    }

    /// <summary>Editorial for SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.</summary>
    private static string ModuleOf(Assembly assembly) =>
        assembly.GetName().Name![ModulePrefix.Length..].Split('.')[0];

    /// <summary>
    /// The integration events whose constructors the type's code calls or hands to an expression tree, including the code the
    /// compiler moves into nested types: lambdas, closures and async state machines.
    /// </summary>
    private static IEnumerable<Type> CreatedEvents(Type type) =>
        WithNested(type)
            .SelectMany(candidate => candidate.GetMethods(Declared).Cast<MethodBase>().Concat(candidate.GetConstructors(Declared)))
            .SelectMany(ConstructorsUsed)
            .Select(constructor => constructor.DeclaringType!)
            .Where(IsIntegrationEvent);

    private static bool IsIntegrationEvent(Type type)
    {
        for (var baseType = type.BaseType; baseType is not null; baseType = baseType.BaseType)
        {
            if (baseType.FullName == IntegrationEvent)
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<Type> WithNested(Type type) =>
        type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic).SelectMany(WithNested).Prepend(type);

    /// <summary>Constructors a method body names with newobj, call or ldtoken.</summary>
    private static IEnumerable<ConstructorInfo> ConstructorsUsed(MethodBase method)
    {
        var il = method.GetMethodBody()?.GetILAsByteArray() ?? [];
        var typeArguments = method.DeclaringType is { IsGenericType: true } declaring ? declaring.GetGenericArguments() : null;
        var methodArguments = method is MethodInfo { IsGenericMethod: true } ? method.GetGenericArguments() : null;

        var position = 0;
        while (position < il.Length)
        {
            var twoBytes = il[position] == 0xFE;
            var code = OpCodesByValue[twoBytes ? unchecked((short)(0xFE00 | il[position + 1])) : il[position]];
            position += twoBytes ? 2 : 1;

            if ((code == OpCodes.Newobj || code == OpCodes.Call || code == OpCodes.Ldtoken)
                && method.Module.ResolveMember(BitConverter.ToInt32(il, position), typeArguments, methodArguments) is ConstructorInfo constructor)
            {
                yield return constructor;
            }

            position += OperandSize(code, il, position);
        }
    }

    private static int OperandSize(OpCode code, byte[] il, int position) => code.OperandType switch
    {
        OperandType.InlineNone => 0,
        OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
        OperandType.InlineVar => 2,
        OperandType.InlineI8 or OperandType.InlineR => 8,
        OperandType.InlineSwitch => 4 + (4 * BitConverter.ToInt32(il, position)),
        _ => 4,
    };
}
