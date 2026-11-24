using System.Text.Json;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// The error codes a caller can receive, as the architecture tests export them for the frontend
/// (web/app/src/i18n/error-codes.json): one array of each code, its status and its English fallback message. A test reads the message from
/// here, so a later rewording touches only the exported file.
/// </summary>
public static class ErrorCodeCatalog
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static readonly Lazy<IReadOnlyDictionary<string, ErrorCodeEntry>> Entries = new(Load);

    /// <summary>The English message the exported file gives <paramref name="code"/>.</summary>
    public static string MessageOf(string code)
    {
        Entries.Value.ShouldContainKey(code, $"web/app/src/i18n/error-codes.json has no entry for {code}.");
        return Entries.Value[code].Message;
    }

    private static Dictionary<string, ErrorCodeEntry> Load()
    {
        var path = Path.Combine(RepositoryRoot(), "web", "app", "src", "i18n", "error-codes.json");
        File.Exists(path).ShouldBeTrue($"{path} is missing: the architecture tests write it (ErrorCodesExportTests).");
        var entries = JsonSerializer.Deserialize<List<ErrorCodeEntry>>(File.ReadAllText(path), Web);
        entries.ShouldNotBeNull($"{path} holds no error codes.");
        return entries.ToDictionary(entry => entry.Code, StringComparer.Ordinal);
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "build.proj")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("No build.proj above " + AppContext.BaseDirectory);
    }
}
