using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SmartSolutionsLab.PharmaTraining.Architecture.Tests.Rules;

/// <summary>
/// Azure.Identity is how a published service reads and signs for its own blob container keyless, under its own managed
/// identity (docs/adr/0002), and nothing else: the model's key is its only authentication (the AGENTS.md LLM section), so
/// a credential in <c>BuildingBlocks.Llm</c> or anywhere outside <c>BuildingBlocks.Storage</c> is a second way in nobody
/// decided. Sources are parsed, not grepped: a <c>using</c> (plain, global, static or alias) or a qualified name counts
/// wherever it is, a comment or a string that spells one is prose. A project-wide <c>&lt;Using Include="Azure.Identity"&gt;</c>
/// in a project file counts too.
/// </summary>
public sealed class AzureIdentityStaysInStorage : IArchitectureRule
{
    private const string AzureIdentity = "Azure.Identity";

    private const string AllowedNamespace = "SmartSolutionsLab.PharmaTraining.BuildingBlocks.Storage";

    private const string ScannedTree = "src";

    public string Description => $"Only {AllowedNamespace} (and the namespaces under it) uses {AzureIdentity}; nothing else, BuildingBlocks.Llm least of all, takes a credential of its own.";

    public IEnumerable<string> Violations(ArchitectureModel model)
    {
        Ensure.That(model).IsNotNull();

        var root = RepositoryRoot();
        var tree = Path.Combine(root, ScannedTree);
        if (!Directory.Exists(tree))
        {
            return [];
        }

        var files = Directory.EnumerateFiles(tree, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .Where(path => !path.Contains("/bin/", StringComparison.Ordinal) && !path.Contains("/obj/", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal);
        List<string> violations = [];
        foreach (var path in files)
        {
            if (path.EndsWith(".cs", StringComparison.Ordinal))
            {
                violations.AddRange(Violations(path, File.ReadAllText(Path.Combine(root, path))));
            }
            else if (path.EndsWith("proj", StringComparison.Ordinal) || path.EndsWith(".props", StringComparison.Ordinal) || path.EndsWith(".targets", StringComparison.Ordinal))
            {
                violations.AddRange(ProjectViolations(path, File.ReadAllText(Path.Combine(root, path))));
            }
        }

        return violations;
    }

    /// <summary>The violations in one C# source: each use of Azure.Identity in a file outside the Storage namespace.</summary>
    public static IEnumerable<string> Violations(string path, string source)
    {
        Ensure.That(path).IsNotNullOrWhiteSpace();
        Ensure.That(source).IsNotNull();

        var root = CSharpSyntaxTree.ParseText(source).GetRoot();
        var namespaces = root.DescendantNodes().OfType<BaseNamespaceDeclarationSyntax>().Select(declaration => declaration.Name.ToString()).ToList();
        if (namespaces.Count > 0 && namespaces.All(IsAllowed))
        {
            return [];
        }

        var where = namespaces.Count > 0 ? $"namespace {string.Join(", ", namespaces.Distinct(StringComparer.Ordinal))}" : "no namespace";
        return Uses(root)
            .Select(use => $"{path}:{use.Line}: uses {use.Name} in {where}; only {AllowedNamespace} may use {AzureIdentity}.")
            .ToList();
    }

    private static IEnumerable<(int Line, string Name)> Uses(SyntaxNode root)
    {
        foreach (var directive in root.DescendantNodes().OfType<UsingDirectiveSyntax>())
        {
            if (directive.NamespaceOrType is { } name && IsAzureIdentity(name.ToString()))
            {
                yield return (Line(directive), name.ToString());
            }
        }

        // Qualified names outside the using directives, outermost only: Azure.Identity.ManagedIdentityCredential once,
        // not again as Azure.Identity.
        foreach (var name in root.DescendantNodes().OfType<QualifiedNameSyntax>()
            .Where(name => name.Parent is not QualifiedNameSyntax && name.FirstAncestorOrSelf<UsingDirectiveSyntax>() is null)
            .Where(name => IsAzureIdentity(name.ToString())))
        {
            yield return (Line(name), name.ToString());
        }

        foreach (var access in root.DescendantNodes().OfType<MemberAccessExpressionSyntax>()
            .Where(access => access.Parent is not MemberAccessExpressionSyntax)
            .Where(access => IsAzureIdentity(access.ToString())))
        {
            yield return (Line(access), access.ToString());
        }
    }

    private static IEnumerable<string> ProjectViolations(string path, string project) =>
        Regex.Matches(project, @"<Using\s+Include\s*=\s*""(?<name>[^""]+)""", RegexOptions.None, TimeSpan.FromSeconds(1))
            .Select(match => match.Groups["name"].Value)
            .Where(IsAzureIdentity)
            .Select(name => $"{path}: imports {name} into every file of the project; only {AllowedNamespace} may use {AzureIdentity}.");

    private static bool IsAzureIdentity(string name)
    {
        var trimmed = name.Replace(" ", string.Empty, StringComparison.Ordinal);
        trimmed = trimmed.StartsWith("global::", StringComparison.Ordinal) ? trimmed["global::".Length..] : trimmed;
        return trimmed == AzureIdentity || trimmed.StartsWith(AzureIdentity + ".", StringComparison.Ordinal);
    }

    private static bool IsAllowed(string name) =>
        name == AllowedNamespace || name.StartsWith(AllowedNamespace + ".", StringComparison.Ordinal);

    private static int Line(SyntaxNode node) => node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;

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
