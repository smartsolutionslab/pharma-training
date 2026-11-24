namespace SmartSolutionsLab.PharmaTraining.Architecture.Tests;

/// <summary>
/// Error codes are the contract (#145): web/app/src/i18n/error-codes.json lists every code a caller can receive, with
/// its status and its English fallback message, as one array sorted by code, so the frontend translates by code (#147) and a new code shows up in
/// the file's diff.
/// </summary>
public class ErrorCodesExportTests
{
    private static readonly ArchitectureModel Model = ArchitectureModel.Load();

    private static IReadOnlyList<ErrorCatalogEntry> Entries => ErrorCatalog.Entries(ErrorCatalog.ErrorTypes(Model));

    [Fact]
    public void The_frontends_copy_of_the_error_codes_is_current()
    {
        var expected = ErrorCatalog.Export(Entries);

        var path = Path.Combine(RepositoryRoot(), "web", "app", "src", "i18n", "error-codes.json");
        var actual = File.Exists(path) ? File.ReadAllText(path).ReplaceLineEndings("\n") : string.Empty;
        if (actual != expected)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, expected);
        }

        actual.ShouldBe(expected, "web/app/src/i18n/error-codes.json was stale and has been rewritten: commit it.");
    }

    /// <summary>
    /// The export lists every code the API can answer (#147), a domain error's included: each needs a status, so a domain
    /// error no <c>*Failures</c> method maps yet fails here until one gives it the status it is answered with.
    /// </summary>
    [Fact]
    public void Every_code_the_api_can_report_has_a_status_and_is_exported()
    {
        var types = ErrorCatalog.ErrorTypes(Model);

        ErrorCatalog.WithoutStatus(types).Select(probe => probe.Describe()).ShouldBeEmpty();
        Entries.Select(entry => entry.Code).ShouldBe(
            ErrorCatalog.Probe(types).Select(probe => probe.Code).OfType<string>().Distinct().Order(StringComparer.Ordinal));
    }

    [Fact]
    public void The_hosts_generic_codes_are_exported()
    {
        var entries = Entries;

        entries.ShouldContain(new ErrorCatalogEntry("invalid-argument", 400, "The request is invalid."));
        entries.ShouldContain(new ErrorCatalogEntry("already-exists", 409, "Something with that name or key already exists."));
        entries.ShouldContain(new ErrorCatalogEntry("model-not-configured", 503, "The AI model is not configured."));
    }

    [Fact]
    public void The_blueprints_question_count_has_one_message_whichever_layer_refuses_it()
    {
        Entries.Single(entry => entry.Code == "editorial.invalid-question-count")
            .ShouldBe(new ErrorCatalogEntry("editorial.invalid-question-count", 400, "A test has 1 to 50 questions."));
    }

    [Fact]
    public void Four_eyes_refusals_read_alike()
    {
        var entries = Entries;

        foreach (var code in new[] { "editorial.four-eyes", "editorial.publisher-approved-questions" })
        {
            var entry = entries.SingleOrDefault(candidate => candidate.Code == code);
            entry.ShouldNotBeNull($"{code} is exported");
            entry.Message.ShouldStartWith("Under four eyes, ", Case.Sensitive, code);
            entry.Status.ShouldBe(403, code);
        }
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
