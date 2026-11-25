using System.Net;
using System.Text.Json.Nodes;

namespace SmartSolutionsLab.PharmaTraining.DemoSeeder;

/// <summary>What the edge answered: the status, the JSON body if any, and a failure's error code (docs/adr/0027).</summary>
internal sealed record EdgeAnswer(HttpStatusCode Status, JsonNode? Body, string? Code)
{
    public bool Succeeded => (int)Status is >= 200 and < 300;
}
