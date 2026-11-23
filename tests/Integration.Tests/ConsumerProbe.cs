using System.Reflection;
using Aspire.Hosting;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// A module's Application compiled on the fly, for what <see cref="MessagingTopology"/> reads from one that no module has
/// yet: its consumers reference the contracts and, where a test asks for it, a dependency of their own,
/// <c>Probe.Dependency</c>, which is written beside it but never handed to <see cref="MessagingTopology"/>, as the
/// Application's packages (Wolverine, logging) are not.
/// </summary>
public sealed class ConsumerProbe : IDisposable
{
    private const string MetadataKey = "MessagingAssembly";

    private const string DependencySource = """
        namespace Probe.Dependency;

        public interface IMessageContext;
        """;

    private readonly string directory;

    private ConsumerProbe(string directory, string application, string contracts)
    {
        this.directory = directory;
        Application = application;
        Contracts = contracts;
    }

    /// <summary>The probe module's Application assembly.</summary>
    public string Application { get; }

    /// <summary>The contracts the AppHost's build recorded.</summary>
    public string Contracts { get; }

    /// <summary>Compiles <paramref name="source"/> as the Application of the module <paramref name="module"/>.</summary>
    public static ConsumerProbe Compile(string module, string source)
    {
        var contracts = typeof(MessagingTopology).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Where(attribute => attribute.Key == MetadataKey)
            .Select(attribute => attribute.Value!)
            .Single(path => Path.GetFileNameWithoutExtension(path) == "Contracts");
        var directory = Directory.CreateTempSubdirectory("consumer-probe-").FullName;
        List<MetadataReference> references =
        [
            .. ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
                .Where(path => Path.GetFileName(path).StartsWith("System.", StringComparison.Ordinal) || Path.GetFileName(path) == "netstandard.dll")
                .Select(path => MetadataReference.CreateFromFile(path)),
            MetadataReference.CreateFromFile(contracts),
        ];

        var dependency = Path.Combine(directory, "Probe.Dependency.dll");
        Emit("Probe.Dependency", DependencySource, references, dependency);
        references.Add(MetadataReference.CreateFromFile(dependency));
        var application = Path.Combine(directory, $"SmartSolutionsLab.PharmaTraining.Modules.{module}.Application.dll");
        Emit(Path.GetFileNameWithoutExtension(application), source, references, application);
        return new ConsumerProbe(directory, application, contracts);
    }

    public void Dispose() => Directory.Delete(directory, recursive: true);

    private static void Emit(string name, string source, IEnumerable<MetadataReference> references, string path)
    {
        var compilation = CSharpCompilation.Create(
            name,
            [CSharpSyntaxTree.ParseText(source, cancellationToken: TestContext.Current.CancellationToken)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        var result = compilation.Emit(path, cancellationToken: TestContext.Current.CancellationToken);
        result.Success.ShouldBeTrue(string.Join(Environment.NewLine, result.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)));
    }
}
