using System.Text.Json.Nodes;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.ContentReviewing;

/// <summary>
/// The model's answer as JSON, written by hand in the shape the reviewer asks for (<c>{"hints":[{"chapter":1,
/// "kind":"Gap","text":"…"}]}</c>), so a test reads like what a model sends.
/// </summary>
internal static class ReviewAnswers
{
    public static JsonObject Hint(int chapter, string kind, string text) =>
        new() { ["chapter"] = chapter, ["kind"] = kind, ["text"] = text };

    public static string Answer(params JsonObject[] hints) =>
        new JsonObject { ["hints"] = new JsonArray([.. hints]) }.ToJsonString();

    /// <summary>A sound answer with one readability hint per chapter number given.</summary>
    public static string Sound(params int[] chapters) =>
        Answer([.. chapters.Select(chapter => Hint(chapter, "Readability", $"Kapitel {chapter}: der erste Satz ist sehr lang."))]);
}
