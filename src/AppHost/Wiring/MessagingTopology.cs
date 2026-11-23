using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;

namespace Aspire.Hosting;

/// <summary>
/// What each module puts on the broker and takes from it, derived from its code without a list of modules (docs/adr/0011):
/// the exchanges of the integration events its <c>*Consumer</c> classes handle (a handler of anything else grants nothing), and of the integration events declared in
/// its own contract namespace (<c>src/Contracts/&lt;Module&gt;/</c>). Wolverine names an event's exchange after the event's
/// full CLR name. An event a module both publishes and consumes is local to its host and never on the broker
/// (docs/adr/0004), so it is in neither list; that is the rule the host's own messaging follows
/// (<c>src/Host/Wiring/Messaging.cs</c>). The assemblies are read as metadata alone, from the paths AppHost.csproj records
/// when it builds every module's Application and the contracts: the AppHost loads no module code.
/// </summary>
public static class MessagingTopology
{
    private const string MetadataKey = "MessagingAssembly";

    private const string ContractsNamespace = "SmartSolutionsLab.PharmaTraining.Contracts";

    private const string IntegrationEvent = ContractsNamespace + ".IntegrationEvent";

    private const string ContractsAssembly = "Contracts";

    private static readonly HashSet<string> HandlerMethodNames = new(["Handle", "HandleAsync", "Consume", "ConsumeAsync"], StringComparer.Ordinal);

    /// <summary>
    /// The exchanges the module's service binds its queue to (<c>Consumed</c>) and those it publishes to
    /// (<c>Published</c>), each sorted ordinally. The module is matched ignoring case.
    /// </summary>
    public static (IReadOnlyList<string> Consumed, IReadOnlyList<string> Published) Of(string module) =>
        Of(module, [.. typeof(MessagingTopology).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Where(attribute => attribute.Key == MetadataKey && !string.IsNullOrEmpty(attribute.Value))
            .Select(attribute => attribute.Value!)]);

    /// <summary>
    /// What <see cref="Of(string)"/> answers, read from the given assemblies instead of those the AppHost's build recorded:
    /// the module's Application (<c>SmartSolutionsLab.PharmaTraining.Modules.&lt;Module&gt;.Application.dll</c>) and the
    /// contracts (<c>Contracts.dll</c>) among them. No other assembly needs to be given.
    /// </summary>
    public static (IReadOnlyList<string> Consumed, IReadOnlyList<string> Published) Of(string module, IReadOnlyCollection<string> assemblyPaths)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(module);
        ArgumentNullException.ThrowIfNull(assemblyPaths);

        var applicationName = $"SmartSolutionsLab.PharmaTraining.Modules.{module}.Application";
        var applicationPath = assemblyPaths.SingleOrDefault(path => string.Equals(Path.GetFileNameWithoutExtension(path), applicationName, StringComparison.OrdinalIgnoreCase));
        var contractsPath = assemblyPaths.SingleOrDefault(path => string.Equals(Path.GetFileNameWithoutExtension(path), ContractsAssembly, StringComparison.Ordinal));
        if (applicationPath is null || !File.Exists(applicationPath) || contractsPath is null || !File.Exists(contractsPath))
        {
            throw new InvalidOperationException(
                $"The AppHost's build recorded no Application assembly of the module '{module}' ({applicationName}) beside the contracts; AppHost.csproj references every src/Modules/*/*.Application/*.Application.csproj and src/Contracts, so build the AppHost again.");
        }

        var resolver = new PathAssemblyResolver([.. Directory.EnumerateFiles(RuntimeEnvironment.GetRuntimeDirectory(), "*.dll"), .. assemblyPaths.Where(File.Exists)]);
        using var context = new MetadataLoadContext(resolver);
        var contracts = context.LoadFromAssemblyPath(contractsPath);
        var application = context.LoadFromAssemblyPath(applicationPath);
        using var image = new PEReader(File.OpenRead(applicationPath));
        var metadata = image.GetMetadataReader();

        var own = $"{ContractsNamespace}.{module}";
        var events = contracts.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false } && IsIntegrationEvent(type))
            .ToList();
        var owned = events
            .Where(type => string.Equals(type.Namespace, own, StringComparison.OrdinalIgnoreCase))
            .Select(type => type.FullName!)
            .ToHashSet(StringComparer.Ordinal);
        // Only an integration event is ever an exchange: a helper named like a handler, or a typo in one, grants nothing.
        var integrationEvents = events.Select(type => type.FullName!).ToHashSet(StringComparer.Ordinal);
        var consumed = application.GetExportedTypes()
            .Where(type => type.Name.EndsWith("Consumer", StringComparison.Ordinal) && type is { IsClass: true, IsAbstract: false })
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Where(method => HandlerMethodNames.Contains(method.Name))
            .Select(method => FirstParameterType(metadata, method.MetadataToken))
            .OfType<string>()
            .Where(integrationEvents.Contains)
            .ToHashSet(StringComparer.Ordinal);

        return (
            [.. consumed.Where(exchange => !owned.Contains(exchange)).Order(StringComparer.Ordinal)],
            [.. owned.Where(exchange => !consumed.Contains(exchange)).Order(StringComparer.Ordinal)]);
    }

    /// <summary>
    /// The full name of a handler method's first parameter, the message it handles, or nothing for a method without one.
    /// The signature is read by name alone: <see cref="MethodBase.GetParameters"/> would resolve every parameter's
    /// assembly, and a consumer may take services of any assembly beside its message (AGENTS.md), which the AppHost
    /// does not have at hand.
    /// </summary>
    private static string? FirstParameterType(MetadataReader metadata, int token)
    {
        var signature = metadata.GetMethodDefinition((MethodDefinitionHandle)MetadataTokens.EntityHandle(token)).DecodeSignature(new SignatureTypeNames(metadata), null);
        return signature.ParameterTypes.IsEmpty ? null : signature.ParameterTypes[0];
    }

    private static bool IsIntegrationEvent(Type type)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current.FullName == IntegrationEvent)
            {
                return true;
            }
        }

        return false;
    }
}
