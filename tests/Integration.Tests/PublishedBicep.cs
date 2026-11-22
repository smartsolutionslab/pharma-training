using System.Text.RegularExpressions;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// What one publish wrote: <c>main.bicep</c> and one module per Azure resource, <c>&lt;resource&gt;/&lt;resource&gt;.bicep</c>,
/// read as text. A resource published as a Container App declares <c>Microsoft.App/containerApps</c> in its module.
/// </summary>
public sealed partial class PublishedBicep
{
    private PublishedBicep(IReadOnlyDictionary<string, string> files) => Files = files;

    /// <summary>Every file written, by its path relative to the output directory with '/' between folders.</summary>
    public IReadOnlyDictionary<string, string> Files { get; }

    /// <summary>The resources published as Container Apps, by name, each with its module's Bicep.</summary>
    public IReadOnlyDictionary<string, string> ContainerApps =>
        Files.Where(file => ContainerAppDeclaration().IsMatch(file.Value))
            .ToDictionary(file => ModuleName(file.Key), file => file.Value, StringComparer.Ordinal);

    public static PublishedBicep Read(string directory)
    {
        var files = Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                .ToDictionary(
                    path => Path.GetRelativePath(directory, path).Replace(Path.DirectorySeparatorChar, '/'),
                    File.ReadAllText,
                    StringComparer.Ordinal)
            : [];
        return new PublishedBicep(files);
    }

    /// <summary>The module a resource was written to, <c>&lt;resource&gt;/&lt;resource&gt;.bicep</c>.</summary>
    public string Module(string resource)
    {
        var path = $"{resource}/{resource}.bicep";
        Files.ShouldContainKey(path, $"Publishing wrote no module for '{resource}'. It wrote: {string.Join(", ", Files.Keys.Order(StringComparer.Ordinal))}");
        return Files[path];
    }

    /// <summary>The parameters a module declares <c>@secure()</c>: secrets it receives as values.</summary>
    public static IReadOnlyList<string> SecureParameters(string bicep) =>
        [.. SecureParameter().Matches(bicep).Select(match => match.Groups["name"].Value)];

    /// <summary>The block that follows <c>&lt;property&gt;: {</c>, up to its closing brace, or empty where there is none.</summary>
    public static string Block(string bicep, string property)
    {
        var start = Regex.Match(bicep, $@"\b{Regex.Escape(property)}:\s*\{{", RegexOptions.None, TimeSpan.FromSeconds(1));
        if (!start.Success)
        {
            return string.Empty;
        }

        var depth = 0;
        for (var index = start.Index + start.Length - 1; index < bicep.Length; index++)
        {
            if (bicep[index] == '{')
            {
                depth++;
            }
            else if (bicep[index] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return bicep[start.Index..(index + 1)];
                }
            }
        }

        return bicep[start.Index..];
    }

    private static string ModuleName(string path) => path.Split('/')[0];

    [GeneratedRegex(@"'Microsoft\.App/containerApps@")]
    private static partial Regex ContainerAppDeclaration();

    [GeneratedRegex(@"@secure\(\)\s*param\s+(?<name>\w+)")]
    private static partial Regex SecureParameter();
}
