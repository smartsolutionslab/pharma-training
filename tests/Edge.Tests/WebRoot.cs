namespace SmartSolutionsLab.PharmaTraining.Edge.Tests;

/// <summary>
/// A web root of the test's own, in a fresh temporary directory: the web app's build (<c>index.html</c> and
/// <c>assets/app.js</c>), as the edge's image carries it in <c>wwwroot</c>, or nothing at all. Removed on dispose.
/// </summary>
public sealed class WebRoot : IDisposable
{
    /// <summary>A text only the test's <c>index.html</c> holds, to tell the app from any other answer.</summary>
    public const string AppMarker = "edge-tests-app";

    public const string AssetPath = "/assets/app.js";

    public const string AssetContent = "console.log('edge-tests-asset');";

    /// <summary>
    /// The text of the inline script in the test's <c>index.html</c>, exactly as it stands between its tags, as the web app's
    /// own <c>index.html</c> sets the theme and language before the first paint.
    /// </summary>
    public const string InlineScript = "\n      document.documentElement.dataset.edgeTests = 'inline';\n    ";

    private WebRoot(string path) => Path = path;

    public string Path { get; }

    /// <summary>A web root holding the web app's build.</summary>
    public static WebRoot WithTheApp()
    {
        var root = Empty();
        File.WriteAllText(
            System.IO.Path.Combine(root.Path, "index.html"),
            $"<!doctype html><html lang=\"en\"><head><title>{AppMarker}</title><script>{InlineScript}</script></head><body><div id=\"app\"></div><script type=\"module\" src=\"{AssetPath}\"></script></body></html>");
        Directory.CreateDirectory(System.IO.Path.Combine(root.Path, "assets"));
        File.WriteAllText(System.IO.Path.Combine(root.Path, "assets", "app.js"), AssetContent);
        return root;
    }

    /// <summary>A web root without <c>index.html</c>: the edge as run mode starts it, beside the web app's dev server.</summary>
    public static WebRoot Empty()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pharma-training-edge-tests", Guid.CreateVersion7().ToString("N"));
        Directory.CreateDirectory(path);
        return new WebRoot(path);
    }

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
