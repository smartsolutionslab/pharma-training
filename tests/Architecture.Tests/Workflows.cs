using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace SmartSolutionsLab.PharmaTraining.Architecture.Tests;

/// <summary>
/// The workflows in <c>.github/workflows</c>, read as YAML, for the rules that check what a workflow does rather than how
/// it is spelled.
/// </summary>
public static class Workflows
{
    /// <summary>The workflow's root mapping, or the reason it has none (missing, not valid YAML, not a mapping).</summary>
    public static (YamlMappingNode? Root, string? Problem) Load(string name)
    {
        var path = Path.Combine(RepositoryRoot(), ".github", "workflows", name);
        if (!File.Exists(path))
        {
            return (null, $".github/workflows/{name} is missing.");
        }

        var stream = new YamlStream();
        try
        {
            using var reader = new StreamReader(path);
            stream.Load(reader);
        }
        catch (YamlException exception)
        {
            return (null, $".github/workflows/{name}:{exception.Start.Line}: is not valid YAML: {exception.Message}");
        }

        if (stream.Documents.Count != 1 || stream.Documents[0].RootNode is not YamlMappingNode root)
        {
            return (null, $".github/workflows/{name} is not one YAML mapping.");
        }

        return (root, null);
    }

    /// <summary>The value under <paramref name="key"/> when <paramref name="node"/> is a mapping that has it.</summary>
    public static YamlNode? Value(YamlNode? node, string key) =>
        node is YamlMappingNode mapping && mapping.Children.TryGetValue(new YamlScalarNode(key), out var value) ? value : null;

    /// <summary>The scalar under <paramref name="key"/>, or null.</summary>
    public static string? Scalar(YamlNode? node, string key) => Value(node, key) is YamlScalarNode scalar ? scalar.Value : null;

    /// <summary>Every job, by its id.</summary>
    public static IEnumerable<(string Id, YamlNode Job)> Jobs(YamlNode workflow) =>
        Value(workflow, "jobs") is YamlMappingNode jobs
            ? jobs.Children.Select(job => (job.Key is YamlScalarNode { Value: { } id } ? id : job.Key.ToString(), job.Value))
            : [];

    /// <summary>A job's steps.</summary>
    public static IEnumerable<YamlNode> Steps(YamlNode job) =>
        Value(job, "steps") is YamlSequenceNode steps ? steps.Children : [];

    /// <summary>The entries of a mapping's <c>env</c>, as name and scalar value.</summary>
    public static IEnumerable<(string Name, string Value)> Env(YamlNode? node) =>
        Value(node, "env") is YamlMappingNode env
            ? env.Children
                .Where(entry => entry.Key is YamlScalarNode { Value: not null })
                .Select(entry => (((YamlScalarNode)entry.Key).Value!, entry.Value is YamlScalarNode { Value: { } value } ? value : string.Empty))
            : [];

    /// <summary>The project root: the directory above the test output that holds build.proj.</summary>
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
