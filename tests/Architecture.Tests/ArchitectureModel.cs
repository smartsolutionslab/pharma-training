using System.Reflection;

namespace SmartSolutionsLab.PharmaTraining.Architecture.Tests;

/// <summary>The assemblies architecture rules look at.</summary>
public sealed class ArchitectureModel
{
    /// <summary>The assemblies of the project's own code that are not a module's, which the architecture tests load.</summary>
    private static readonly string[] UnprefixedProjectAssemblies = ["BuildingBlocks", "Contracts", "ServiceDefaults"];

    private ArchitectureModel(IReadOnlyList<Assembly> moduleAssemblies, IReadOnlyList<Assembly> projectAssemblies)
    {
        ModuleAssemblies = moduleAssemblies;
        ProjectAssemblies = projectAssemblies;
    }

    public IReadOnlyList<Assembly> ModuleAssemblies { get; }

    /// <summary>
    /// Every assembly of the project's own code the architecture tests load: the modules', and the Kernel's,
    /// BuildingBlocks' (its Domain and EventSourcing too), Contracts', ServiceDefaults' and each service host's, which
    /// compiles src/Host.
    /// </summary>
    public IReadOnlyList<Assembly> ProjectAssemblies { get; }

    public static IReadOnlyList<IArchitectureRule> Rules { get; } =
        [.. typeof(ArchitectureModel).Assembly.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false } && typeof(IArchitectureRule).IsAssignableFrom(type))
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .Select(type => (IArchitectureRule)Activator.CreateInstance(type)!)];

    public static ArchitectureModel Load() =>
        new(
            [.. Directory.EnumerateFiles(AppContext.BaseDirectory, "SmartSolutionsLab.PharmaTraining.Modules.*.dll")
                .Order(StringComparer.Ordinal)
                .Select(Assembly.LoadFrom)],
            [.. Directory.EnumerateFiles(AppContext.BaseDirectory, "SmartSolutionsLab.PharmaTraining.*.dll")
                .Order(StringComparer.Ordinal)
                .Select(Assembly.LoadFrom)
                .Concat(UnprefixedProjectAssemblies.Select(Assembly.Load))]);

    /// <summary>A model of the given assemblies, for a rule's test to run it over its probes.</summary>
    public static ArchitectureModel Of(IReadOnlyList<Assembly> moduleAssemblies, IReadOnlyList<Assembly> projectAssemblies) =>
        new(moduleAssemblies, projectAssemblies);
}
