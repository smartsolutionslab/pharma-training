using System.Reflection;
using System.Xml.Linq;
using Aspire.Hosting.ApplicationModel;

namespace Aspire.Hosting;

/// <summary>
/// Every module's service host, found by convention (src/Services/*/Host/Host.csproj) so that adding a module only
/// creates files. Each is named after its module, as its project file records it, and is added through the
/// <c>Projects.*</c> type the Aspire SDK generates for it (AppHost.csproj), whose metadata suppresses the build: Aspire
/// then starts it with <c>dotnet run --no-build</c> from the AppHost's own build, instead of building it again.
/// </summary>
public static class ServiceHostProjects
{
    // ProjectResourceBuilderExtensions.AddProject<TProject>(builder, name), called with the type found at run time.
    private static readonly MethodInfo AddProjectOfMetadata = typeof(ProjectResourceBuilderExtensions)
        .GetMethods(BindingFlags.Public | BindingFlags.Static)
        .Single(method => method.Name == nameof(ProjectResourceBuilderExtensions.AddProject)
            && method.IsGenericMethodDefinition
            && method.GetParameters() is [{ ParameterType: var first }, { ParameterType: var second }]
            && first == typeof(IDistributedApplicationBuilder)
            && second == typeof(string));

    public static IEnumerable<(string Module, string ProjectPath)> ServiceHosts(this IDistributedApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var services = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", "Services"));
        if (!Directory.Exists(services))
        {
            return [];
        }

        return Directory.EnumerateDirectories(services)
            .Select(service => Path.Combine(service, "Host", "Host.csproj"))
            .Where(File.Exists)
            .Order(StringComparer.Ordinal)
            .Select(path => (ModuleName(path), path));
    }

    /// <summary>Adds the service host at <paramref name="projectPath"/> through its generated project metadata.</summary>
    public static IResourceBuilder<ProjectResource> AddServiceHost(this IDistributedApplicationBuilder builder, string name, string projectPath)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);

        var metadata = MetadataTypeOf(Path.GetFullPath(projectPath));
        return (IResourceBuilder<ProjectResource>)AddProjectOfMetadata.MakeGenericMethod(metadata).Invoke(null, [builder, name])!;
    }

    private static Type MetadataTypeOf(string projectPath)
    {
        var matches = typeof(ServiceHostProjects).Assembly.GetTypes()
            .Where(type => type.Namespace == "Projects" && type.IsClass && !type.IsAbstract && typeof(IProjectMetadata).IsAssignableFrom(type))
            .Where(type => Activator.CreateInstance(type) is IProjectMetadata metadata
                && string.Equals(Path.GetFullPath(metadata.ProjectPath), projectPath, StringComparison.Ordinal))
            .ToList();

        return matches switch
        {
            [var metadata] => metadata,
            [] => throw new InvalidOperationException($"The AppHost's build generated no Projects.* type for {projectPath}; AppHost.csproj references every src/Services/*/Host/Host.csproj, so build the AppHost again."),
            _ => throw new InvalidOperationException($"More than one Projects.* type names {projectPath}: {string.Join(", ", matches.Select(type => type.Name))}."),
        };
    }

    private static string ModuleName(string projectPath) =>
        XDocument.Load(projectPath).Descendants("ModuleName").SingleOrDefault()?.Value
            ?? throw new InvalidOperationException($"{projectPath} does not name its module (<ModuleName>).");
}
