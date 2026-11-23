using System.Reflection;
using System.Runtime.InteropServices;

namespace Aspire.Hosting;

/// <summary>
/// What each module puts on the broker and takes from it, derived from its code without a list of modules (docs/adr/0011):
/// the exchanges of the integration events its <c>*Consumer</c> classes handle, and of the integration events declared in
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
    public static (IReadOnlyList<string> Consumed, IReadOnlyList<string> Published) Of(string module)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(module);

        var paths = typeof(MessagingTopology).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Where(attribute => attribute.Key == MetadataKey && !string.IsNullOrEmpty(attribute.Value))
            .Select(attribute => attribute.Value!)
            .ToList();
        var applicationName = $"SmartSolutionsLab.PharmaTraining.Modules.{module}.Application";
        var applicationPath = paths.SingleOrDefault(path => string.Equals(Path.GetFileNameWithoutExtension(path), applicationName, StringComparison.OrdinalIgnoreCase));
        var contractsPath = paths.SingleOrDefault(path => string.Equals(Path.GetFileNameWithoutExtension(path), ContractsAssembly, StringComparison.Ordinal));
        if (applicationPath is null || !File.Exists(applicationPath) || contractsPath is null || !File.Exists(contractsPath))
        {
            throw new InvalidOperationException(
                $"The AppHost's build recorded no Application assembly of the module '{module}' ({applicationName}) beside the contracts; AppHost.csproj references every src/Modules/*/*.Application/*.Application.csproj and src/Contracts, so build the AppHost again.");
        }

        var resolver = new PathAssemblyResolver([.. Directory.EnumerateFiles(RuntimeEnvironment.GetRuntimeDirectory(), "*.dll"), .. paths.Where(File.Exists)]);
        using var context = new MetadataLoadContext(resolver);
        var contracts = context.LoadFromAssemblyPath(contractsPath);
        var application = context.LoadFromAssemblyPath(applicationPath);

        var own = $"{ContractsNamespace}.{module}";
        var owned = contracts.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false } && string.Equals(type.Namespace, own, StringComparison.OrdinalIgnoreCase) && IsIntegrationEvent(type))
            .Select(type => type.FullName!)
            .ToHashSet(StringComparer.Ordinal);
        var consumed = application.GetExportedTypes()
            .Where(type => type.Name.EndsWith("Consumer", StringComparison.Ordinal) && type is { IsClass: true, IsAbstract: false })
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Where(method => HandlerMethodNames.Contains(method.Name) && method.GetParameters().Length > 0)
            .Select(method => method.GetParameters()[0].ParameterType.FullName!)
            .ToHashSet(StringComparer.Ordinal);

        return (
            [.. consumed.Where(exchange => !owned.Contains(exchange)).Order(StringComparer.Ordinal)],
            [.. owned.Where(exchange => !consumed.Contains(exchange)).Order(StringComparer.Ordinal)]);
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
