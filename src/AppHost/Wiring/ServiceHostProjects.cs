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
    // AddBuilt<TProject>, made for the type found at run time. It calls Aspire's AddProject<TProject> directly, so a change
    // to that signature fails the build here instead of at start-up.
    private static readonly MethodInfo AddBuiltOfMetadata = typeof(ServiceHostProjects)
        .GetMethod(nameof(AddBuilt), BindingFlags.NonPublic | BindingFlags.Static)!;

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
        // DoNotWrapExceptions: an exception Aspire throws arrives as itself, not wrapped in TargetInvocationException.
        return (IResourceBuilder<ProjectResource>)AddBuiltOfMetadata.MakeGenericMethod(metadata)
            .Invoke(null, BindingFlags.DoNotWrapExceptions, binder: null, [builder, name], culture: null)!;
    }

    private static IResourceBuilder<ProjectResource> AddBuilt<TProject>(IDistributedApplicationBuilder builder, string name)
        where TProject : IProjectMetadata, new() =>
        builder.AddProject<TProject>(name);

    private static Type MetadataTypeOf(string projectPath)
    {
        var matches = typeof(ServiceHostProjects).Assembly.GetTypes()
            .Where(type => type.Namespace == "Projects" && type.IsClass && !type.IsAbstract && typeof(IProjectMetadata).IsAssignableFrom(type))
            .Where(type => Activator.CreateInstance(type) is IProjectMetadata metadata
                && string.Equals(Path.GetFullPath(metadata.ProjectPath), projectPath, StringComparison.Ordinal))
            .ToList();

        // The SDK generates one type per referenced project, so a path names one type or, with a stale build, none.
        return matches.SingleOrDefault()
            ?? throw new InvalidOperationException($"The AppHost's build generated no Projects.* type for {projectPath}; AppHost.csproj references every src/Services/*/Host/Host.csproj, so build the AppHost again.");
    }

    private static string ModuleName(string projectPath) =>
        XDocument.Load(projectPath).Descendants("ModuleName").SingleOrDefault()?.Value
            ?? throw new InvalidOperationException($"{projectPath} does not name its module (<ModuleName>).");
}
