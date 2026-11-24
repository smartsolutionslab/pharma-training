using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Llm;

namespace SmartSolutionsLab.PharmaTraining.Architecture.Tests;

/// <summary>
/// Every error a caller can receive or a domain rule can report, probed by reflection: each non-abstract
/// <see cref="ApiError"/> and <see cref="DomainError"/> of the modules and of the host's BuildingBlocks is made twice,
/// once with empty constructor arguments and once with filled ones, and a domain error's statuses are found through
/// the <c>*Failures</c> methods of its module's Application that map it.
/// </summary>
public static partial class ErrorCatalog
{
    private const string FailuresSuffix = "Failures";

    private const string UnionAttribute = "UnionAttribute";

    private static readonly JsonSerializerOptions ExportOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>The host's generic refusals live here; the type named is only a way to find the assembly.</summary>
    private static Assembly BuildingBlocks => typeof(ModelNotConfiguredException).Assembly;

    /// <summary>Every error type of the module assemblies and of BuildingBlocks, ordered by name.</summary>
    public static IReadOnlyList<Type> ErrorTypes(ArchitectureModel model)
    {
        Ensure.That(model).IsNotNull();

        return [.. model.ModuleAssemblies
            .Append(BuildingBlocks)
            .Distinct()
            .SelectMany(assembly => assembly.GetTypes())
            .Where(IsError)
            .OrderBy(type => type.FullName, StringComparer.Ordinal)];
    }

    /// <summary>Probes each of <paramref name="types"/> that is an error.</summary>
    public static IReadOnlyList<ErrorProbe> Probe(IEnumerable<Type> types)
    {
        Ensure.That(types).IsNotNull();

        return [.. types.Where(IsError).Select(Probe)];
    }

    /// <summary>
    /// The export's rows: every code a caller can receive (a probe with a status), one row per code, sorted ordinally.
    /// Where a code's types disagree, the first by name wins here; <c>EveryErrorCodeHasOneMessageAndStatus</c> names the
    /// disagreement.
    /// </summary>
    public static IReadOnlyList<ErrorCatalogEntry> Entries(IEnumerable<Type> types) =>
        [.. Probe(types)
            .Where(probe => probe is { Code: not null, Message: not null, Statuses.Count: > 0 })
            .GroupBy(probe => probe.Code!, StringComparer.Ordinal)
            .OrderBy(code => code.Key, StringComparer.Ordinal)
            .Select(code =>
            {
                var first = code.OrderBy(probe => probe.Type.FullName, StringComparer.Ordinal).First();
                return new ErrorCatalogEntry(code.Key, first.Statuses[0], first.Message!);
            })];

    /// <summary>
    /// The codes among <paramref name="types"/> no way of answering gives a status: a domain error no <c>*Failures</c>
    /// method maps. The API can report every code, so each one needs a status before the export can list it.
    /// </summary>
    public static IReadOnlyList<ErrorProbe> WithoutStatus(IEnumerable<Type> types) =>
        [.. Probe(types)
            .Where(probe => probe.Code is not null)
            .GroupBy(probe => probe.Code!, StringComparer.Ordinal)
            .Where(code => code.All(probe => probe.Statuses.Count == 0))
            .OrderBy(code => code.Key, StringComparer.Ordinal)
            .SelectMany(code => code)];

    /// <summary>
    /// The rows as web/app/src/i18n/error-codes.json holds them: one array of <c>{ code, status, message }</c>, sorted by
    /// code ordinally, ending in one line feed.
    /// </summary>
    public static string Export(IEnumerable<ErrorCatalogEntry> entries)
    {
        Ensure.That(entries).IsNotNull();

        var document = new JsonArray();
        foreach (var entry in entries.OrderBy(entry => entry.Code, StringComparer.Ordinal))
        {
            document.Add(new JsonObject
            {
                ["code"] = entry.Code,
                ["status"] = entry.Status,
                ["message"] = entry.Message,
            });
        }

        return document.ToJsonString(ExportOptions).ReplaceLineEndings("\n") + "\n";
    }

    private static bool IsError(Type type) =>
        type is { IsClass: true, IsAbstract: false, ContainsGenericParameters: false }
        && type != typeof(DomainFailure)
        && (typeof(ApiError).IsAssignableFrom(type) || typeof(DomainError).IsAssignableFrom(type));

    private static ErrorProbe Probe(Type type)
    {
        var constructors = type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Where(constructor => !IsCopyConstructor(constructor, type))
            .OrderByDescending(constructor => constructor.GetParameters().Length)
            .ToList();
        if (constructors.Count == 0)
        {
            return new ErrorProbe(type, null, null, null, "it has no constructor to probe", []);
        }

        string? code = null;
        string? failure = null;
        object? firstFilled = null;
        List<string> messages = [];

        foreach (var constructor in constructors)
        {
            var filled = Create(constructor, Filled, out var filledFailure);
            var empty = Create(constructor, Empty, out var emptyFailure);
            failure ??= filledFailure ?? emptyFailure;
            firstFilled ??= filled;

            var (filledCode, filledText) = CodeAndMessage(filled);
            var (emptyCode, emptyText) = CodeAndMessage(empty);
            code ??= filledCode ?? emptyCode;
            messages.AddRange(new[] { filledText, emptyText }.OfType<string>());
        }

        // The filled message is the one a caller reads; the other is the first that differs from it, if one does.
        var filledMessage = CodeAndMessage(firstFilled).Message ?? messages.FirstOrDefault();
        var emptyMessage = messages.FirstOrDefault(message => message != filledMessage) ?? filledMessage;

        return new ErrorProbe(type, code, filledMessage, emptyMessage, failure, Statuses(type, firstFilled));
    }

    private static (string? Code, string? Message) CodeAndMessage(object? error) => error switch
    {
        ApiError api => (api.Code, api.Message),
        DomainError domain => (domain.Code, domain.Message),
        _ => (null, null),
    };

    private static bool IsCopyConstructor(ConstructorInfo constructor, Type type)
    {
        var parameters = constructor.GetParameters();
        return parameters.Length == 1 && parameters[0].ParameterType == type;
    }

    private static object? Create(ConstructorInfo constructor, Func<Type, object?> argument, out string? failure)
    {
        try
        {
            failure = null;
            return constructor.Invoke([.. constructor.GetParameters().Select(parameter => argument(parameter.ParameterType))]);
        }
        catch (TargetInvocationException exception)
        {
            var parameters = string.Join(", ", constructor.GetParameters().Select(parameter => parameter.ParameterType.Name));
            failure = $"its constructor ({parameters}) throws {exception.InnerException?.GetType().Name} on probe arguments, so its message reads them";
            return null;
        }
    }

    private static object? Empty(Type type) => type.IsValueType ? Activator.CreateInstance(type) : null;

    private static object? Filled(Type type)
    {
        if (Nullable.GetUnderlyingType(type) is { } underlying)
        {
            return Filled(underlying);
        }

        if (type == typeof(Guid))
        {
            return Guid.CreateVersion7();
        }

        if (type == typeof(string))
        {
            return "probe";
        }

        if (type.IsEnum)
        {
            var values = Enum.GetValuesAsUnderlyingType(type);
            return values.Length == 0 ? Activator.CreateInstance(type) : Enum.ToObject(type, values.GetValue(values.Length - 1)!);
        }

        if (type.IsPrimitive && type != typeof(bool) && type != typeof(char) && type != typeof(nint) && type != typeof(nuint))
        {
            return Convert.ChangeType(7, type, CultureInfo.InvariantCulture);
        }

        if (type == typeof(decimal))
        {
            return 7m;
        }

        if (type.IsValueType)
        {
            return Activator.CreateInstance(type);
        }

        if (type.IsAbstract || type.IsInterface || type.IsArray || type.ContainsGenericParameters)
        {
            return null;
        }

        return RuntimeHelpers.GetUninitializedObject(type);
    }

    /// <summary>
    /// Every status a caller can receive the error with: an <see cref="ApiError"/>'s own; a domain error's from the
    /// <c>*Failures</c> methods of its module's Application, taking one parameter of its exact type, else of a union
    /// that has it as a case, else <see cref="DomainError"/> itself. A method that throws does not map it.
    /// </summary>
    private static List<int> Statuses(Type type, object? filled)
    {
        switch (filled)
        {
            case ApiError api:
                return [(int)api.Status];
            case DomainError domain:
                var methods = FailuresMethods(type).ToList();
                var exact = methods.Where(method => method.GetParameters()[0].ParameterType == type).ToList();
                if (exact.Count > 0)
                {
                    return Invoke(exact, _ => domain);
                }

                var unions = methods.Where(method => IsUnionOf(method.GetParameters()[0].ParameterType, type)).ToList();
                if (unions.Count > 0)
                {
                    return Invoke(unions, method => method.GetParameters()[0].ParameterType.GetConstructor([type])!.Invoke([domain]));
                }

                return Invoke([.. methods.Where(method => method.GetParameters()[0].ParameterType == typeof(DomainError))], _ => domain);
            default:
                return [];
        }
    }

    private static List<int> Invoke(IEnumerable<MethodInfo> methods, Func<MethodInfo, object> argument)
    {
        List<int> statuses = [];
        foreach (var method in methods)
        {
            try
            {
                if (method.Invoke(null, [argument(method)]) is ApiError mapped)
                {
                    statuses.Add((int)mapped.Status);
                }
            }
            catch (TargetInvocationException)
            {
                // A method that throws for this error does not map it: no caller receives it that way.
            }
        }

        return statuses;
    }

    private static bool IsUnionOf(Type parameter, Type error) =>
        parameter.IsValueType
        && parameter.GetCustomAttributesData().Any(attribute => attribute.AttributeType.Name == UnionAttribute)
        && parameter.GetConstructor([error]) is not null;

    /// <summary>The public static methods of the <c>*Failures</c> classes in the error's module's Application that take one argument and answer an ApiError.</summary>
    private static IEnumerable<MethodInfo> FailuresMethods(Type error)
    {
        var module = ModuleName().Match(error.Assembly.GetName().Name ?? string.Empty);
        if (!module.Success)
        {
            return [];
        }

        Assembly application;
        try
        {
            application = Assembly.Load(new AssemblyName($"{module.Groups["module"].Value}.Application"));
        }
        catch (FileNotFoundException)
        {
            return [];
        }

        return application.GetTypes()
            .Where(type => type is { IsAbstract: true, IsSealed: true } && type.Name.EndsWith(FailuresSuffix, StringComparison.Ordinal))
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Where(method => typeof(ApiError).IsAssignableFrom(method.ReturnType) && method.GetParameters().Length == 1);
    }

    [GeneratedRegex(@"^(?<module>SmartSolutionsLab\.PharmaTraining\.Modules\.[^.]+)\.")]
    private static partial Regex ModuleName();
}
