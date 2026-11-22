using System.Text.Json.Nodes;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.TestDrafting;

/// <summary>
/// The model's answer as JSON, written by hand in the shape the drafter asks for (<c>{"questions":[{"chapter":1,
/// "type":"SingleChoice","text":"…","options":[{"text":"…","correct":true}]}]}</c>), so a test reads like what a model sends.
/// </summary>
internal static class DraftAnswers
{
    public static JsonObject Question(int chapter, string type, string text, params (string Text, bool Correct)[] options) =>
        new()
        {
            ["chapter"] = chapter,
            ["type"] = type,
            ["text"] = text,
            ["options"] = new JsonArray([.. options.Select(option => new JsonObject { ["text"] = option.Text, ["correct"] = option.Correct })]),
        };

    /// <summary>A sound single-choice question about the chapter numbered <paramref name="chapter"/>: four options, one correct.</summary>
    public static JsonObject SingleChoice(int chapter) =>
        Question(chapter, "SingleChoice", $"Was gilt laut Kapitel {chapter}?", ("Das Richtige", true), ("Das Falsche", false), ("Das Andere", false), ("Das Übrige", false));

    public static string Answer(params JsonObject[] questions) =>
        new JsonObject { ["questions"] = new JsonArray([.. questions]) }.ToJsonString();

    /// <summary>A sound answer with one single-choice question per chapter number given.</summary>
    public static string Sound(params int[] chapters) => Answer([.. chapters.Select(SingleChoice)]);
}
