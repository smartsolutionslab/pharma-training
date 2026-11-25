using System.Xml;
using System.Xml.Linq;

namespace SmartSolutionsLab.PharmaTraining.Architecture.Tests.Rules;

/// <summary>
/// <c>Solution.slnx</c> is what an IDE opens (docs/adr/fabric/base-core.md); <c>build.proj</c> builds without it, so
/// nothing else notices when it breaks. Rider, Visual Studio and <c>dotnet sln</c> refuse a solution in which two projects
/// of one name share a solution folder ("Project name 'Host' already exists in the 'Root' solution folder"): every
/// service host is <c>Host.csproj</c>, so each needs a folder of its own, <c>/src/Services/&lt;Module&gt;/</c>, as
/// <c>dotnet sln Solution.slnx add --solution-folder src/Services/&lt;Module&gt; &lt;project&gt;</c> gives it. A project under
/// <c>src/</c> or <c>tests/</c> the solution does not list is missing from the IDE, and a listed path that no longer exists
/// fails to load.
/// </summary>
public sealed class SolutionListsEveryProjectOncePerFolder : IArchitectureRule
{
    private const string Solution = "Solution.slnx";

    private static readonly string[] ProjectRoots = ["src", "tests"];

    public string Description => "Solution.slnx lists every project under src/ and tests/, each path existing, and no two projects of one name in one solution folder.";

    public IEnumerable<string> Violations(ArchitectureModel model)
    {
        Ensure.That(model).IsNotNull();

        var root = RepositoryRoot();
        var path = Path.Combine(root, Solution);
        if (!File.Exists(path))
        {
            return [$"{Solution} is missing; it is what an IDE opens."];
        }

        XDocument solution;
        try
        {
            solution = XDocument.Load(path);
        }
        catch (XmlException exception)
        {
            return [$"{Solution} is not valid XML: {exception.Message}"];
        }

        var listed = solution.Descendants("Project")
            .Select(project => (
                Path: ((string?)project.Attribute("Path") ?? string.Empty).Replace('\\', '/'),
                Folder: (string?)project.Parent?.Attribute("Name") ?? "/"))
            .ToList();

        List<string> violations = [];
        violations.AddRange(listed
            .GroupBy(project => (project.Folder, Name: Path.GetFileNameWithoutExtension(project.Path)))
            .Where(group => group.Count() > 1)
            .Select(group => $"{Solution}: the solution folder {group.Key.Folder} holds {group.Count()} projects named '{group.Key.Name}' ({string.Join(", ", group.Select(project => project.Path))}); IDEs refuse to open it. Give each a folder of its own (dotnet sln add --solution-folder)."));

        violations.AddRange(listed
            .Where(project => !File.Exists(Path.Combine(root, project.Path)))
            .Select(project => $"{Solution} lists {project.Path}, which does not exist."));

        var paths = listed.Select(project => project.Path).ToHashSet(StringComparer.Ordinal);
        violations.AddRange(ProjectRoots
            .SelectMany(directory => Directory.EnumerateFiles(Path.Combine(root, directory), "*.csproj", SearchOption.AllDirectories))
            .Select(project => Path.GetRelativePath(root, project).Replace('\\', '/'))
            .Where(project => !project.Split('/').Any(segment => segment is "bin" or "obj" or "node_modules"))
            .Where(project => !paths.Contains(project))
            .Order(StringComparer.Ordinal)
            .Select(project => $"{Solution} does not list {project}; add it with dotnet sln Solution.slnx add --solution-folder <its directory's parent> {project}."));

        return violations;
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "build.proj")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("build.proj not found above the test output directory.");
    }
}
