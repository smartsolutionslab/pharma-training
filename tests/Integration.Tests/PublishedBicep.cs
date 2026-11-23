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

    /// <summary>
    /// The symbols a Bicep file declares, with the line each is on: every <c>param</c>, <c>var</c>, <c>resource</c>,
    /// <c>module</c>, <c>output</c>, <c>type</c> and <c>func</c> name.
    /// </summary>
    public static IReadOnlyList<(string Name, int Line)> DeclaredIdentifiers(string bicep) =>
        [.. Declaration().Matches(bicep).Select(match => (match.Groups["name"].Value, bicep.AsSpan(0, match.Index).Count('\n') + 1))];

    /// <summary>The block that follows <c>&lt;property&gt;: {</c>, up to its closing brace, or empty where there is none.</summary>
    public static string Block(string bicep, string property)
    {
        var start = Regex.Match(bicep, $@"\b{Regex.Escape(property)}:\s*\{{", RegexOptions.None, TimeSpan.FromSeconds(1));
        return start.Success ? BlockAt(bicep, start.Index, start.Index + start.Length - 1) : string.Empty;
    }

    /// <summary>
    /// The role assignments to an identity's principal (<c>&lt;identity&gt;.outputs.principalId</c>), in every module
    /// main.bicep hands that principal to and in the identity's own module.
    /// </summary>
    public IReadOnlyList<PublishedRoleAssignment> RoleAssignmentsOf(string identity)
    {
        var symbol = identity.Replace('-', '_');
        var main = Files.GetValueOrDefault("main.bicep") ?? string.Empty;
        List<PublishedRoleAssignment> assignments = [];
        foreach (Match module in ModuleDeclaration().Matches(main))
        {
            var path = module.Groups["path"].Value;
            var declaration = BlockAt(main, module.Index, module.Index + module.Length - 1);
            var handsPrincipal = Regex.IsMatch(declaration, $@":\s*{Regex.Escape(symbol)}\.outputs\.principalId\b", RegexOptions.None, TimeSpan.FromSeconds(1));
            if ((handsPrincipal || module.Groups["symbol"].Value == symbol) && Files.TryGetValue(path, out var bicep))
            {
                assignments.AddRange(RoleAssignments(path, bicep));
            }
        }

        return assignments;
    }

    /// <summary>Every role assignment in every file written, wherever its principal comes from.</summary>
    public IReadOnlyList<PublishedRoleAssignment> AllRoleAssignments() =>
        [.. Files.Where(file => file.Key.EndsWith(".bicep", StringComparison.Ordinal)).SelectMany(file => RoleAssignments(file.Key, file.Value))];

    /// <summary>
    /// The role assignments one Bicep file declares: the role's id and the type and name of the resource it is scoped to,
    /// <c>resourceGroup</c> where it names no scope. A name that is no literal is kept as written.
    /// </summary>
    public static IReadOnlyList<PublishedRoleAssignment> RoleAssignments(string file, string bicep)
    {
        var resources = Resources(bicep);
        return [.. resources.Values
            .Where(resource => resource.Type == "Microsoft.Authorization/roleAssignments")
            .Select(resource =>
            {
                var role = RoleDefinition().Match(resource.Body).Groups["role"].Value.ToLowerInvariant();
                var scope = Scope().Match(resource.Body);
                if (!scope.Success)
                {
                    return new PublishedRoleAssignment(file, role, "resourceGroup", string.Empty);
                }

                return resources.TryGetValue(scope.Groups["scope"].Value, out var target)
                    ? new PublishedRoleAssignment(file, role, target.Type, target.Name)
                    : new PublishedRoleAssignment(file, role, "unknown", scope.Groups["scope"].Value);
            })];
    }

    /// <summary>The names of the Key Vault secrets a Container App reads as Key Vault references (<c>keyVaultUrl</c>).</summary>
    public static IReadOnlyList<string> KeyVaultSecretsReferenced(string bicep)
    {
        var resources = Resources(bicep);
        return [.. KeyVaultUrl().Matches(bicep)
            .Select(match => resources.TryGetValue(match.Groups["secret"].Value, out var secret) ? secret.Name : match.Groups["secret"].Value)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];
    }

    /// <summary>Every resource a Bicep file declares, existing ones too, by its symbol: its type, name and body.</summary>
    private static Dictionary<string, (string Type, string Name, string Body)> Resources(string bicep) =>
        ResourceDeclaration().Matches(bicep).ToDictionary(
            match => match.Groups["symbol"].Value,
            match =>
            {
                var body = BlockAt(bicep, match.Index, match.Index + match.Length - 1);
                var name = ResourceName().Match(body).Groups["name"].Value;
                return (match.Groups["type"].Value, name.Length > 1 && name[0] == '\'' && name[^1] == '\'' ? name[1..^1] : name, body);
            },
            StringComparer.Ordinal);

    /// <summary>The text from <paramref name="start"/> to the brace that closes the one at <paramref name="openBrace"/>.</summary>
    private static string BlockAt(string bicep, int start, int openBrace)
    {
        var depth = 0;
        for (var index = openBrace; index < bicep.Length; index++)
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
                    return bicep[start..(index + 1)];
                }
            }
        }

        return bicep[start..];
    }

    private static string ModuleName(string path) => path.Split('/')[0];

    [GeneratedRegex(@"'Microsoft\.App/containerApps@")]
    private static partial Regex ContainerAppDeclaration();

    [GeneratedRegex(@"^[ \t]*(param|var|resource|module|output|type|func)[ \t]+(?<name>[A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Multiline)]
    private static partial Regex Declaration();

    [GeneratedRegex(@"@secure\(\)\s*param\s+(?<name>\w+)")]
    private static partial Regex SecureParameter();

    [GeneratedRegex(@"^module\s+(?<symbol>\w+)\s+'(?<path>[^']+)'\s*=\s*\{", RegexOptions.Multiline)]
    private static partial Regex ModuleDeclaration();

    [GeneratedRegex(@"^resource\s+(?<symbol>\w+)\s+'(?<type>[^'@]+)@[^']*'(\s+existing)?\s*=\s*\{", RegexOptions.Multiline)]
    private static partial Regex ResourceDeclaration();

    [GeneratedRegex(@"^\s*name:\s*(?<name>.+?)\s*$", RegexOptions.Multiline)]
    private static partial Regex ResourceName();

    [GeneratedRegex(@"roleDefinitions',\s*'(?<role>[0-9a-fA-F-]{36})'")]
    private static partial Regex RoleDefinition();

    [GeneratedRegex(@"^\s*scope:\s*(?<scope>\w+)\s*$", RegexOptions.Multiline)]
    private static partial Regex Scope();

    [GeneratedRegex(@"keyVaultUrl:\s*(?<secret>\w+)\.properties\.secretUri")]
    private static partial Regex KeyVaultUrl();
}
