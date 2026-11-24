using System.Text.Json.Nodes;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// The OpenAPI check of Assessment's schemas, on a document made for it: a property that names a solution, an answer
/// key or correctness is found, however deep, whatever its casing.
/// </summary>
public class OpenApiSolutionNamesTests
{
    [Fact]
    public void A_property_naming_a_solution_an_answer_key_or_correctness_is_found_however_deep()
    {
        var document = new JsonObject
        {
            ["paths"] = new JsonObject
            {
                ["/assessment/papers"] = new JsonObject
                {
                    ["get"] = new JsonObject
                    {
                        ["responses"] = new JsonObject
                        {
                            ["200"] = new JsonObject
                            {
                                ["content"] = new JsonObject
                                {
                                    ["application/json"] = new JsonObject
                                    {
                                        ["schema"] = new JsonObject { ["$ref"] = "#/components/schemas/AssessmentPaperDto" },
                                    },
                                },
                            },
                        },
                    },
                },
            },
            ["components"] = new JsonObject
            {
                ["schemas"] = new JsonObject
                {
                    ["AssessmentPaperDto"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["questions"] = new JsonObject
                            {
                                ["type"] = "array",
                                ["items"] = new JsonObject { ["$ref"] = "#/components/schemas/AssessmentPaperQuestionDto" },
                            },
                        },
                    },
                    ["AssessmentPaperQuestionDto"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["text"] = new JsonObject { ["type"] = "string" },
                            ["solution"] = new JsonObject { ["type"] = "string" },
                            ["answerKey"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string", ["format"] = "uuid" } },
                            ["isCorrect"] = new JsonObject { ["type"] = "boolean" },
                        },
                    },
                },
            },
        };
        List<string> found = [];

        OpenApiDocumentTests.CollectSolutionNames(document, document["paths"]!["/assessment/papers"], "paths/assessment/papers", found, []);

        found.Select(entry => entry[(entry.LastIndexOf('/') + 1)..]).ShouldBe(["solution", "answerKey", "isCorrect"], ignoreOrder: true);
    }
}
