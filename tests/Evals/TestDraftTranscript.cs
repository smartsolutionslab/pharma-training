using System.Text.Json;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.TestDrafting;

namespace SmartSolutionsLab.PharmaTraining.Evals;

/// <summary>
/// What the AI test draft asked for and got, for <see cref="TestDraftEvaluator"/>: the requested count, each chapter's
/// quota in the order the chapters were sent, the model's answer as read (null when it could not be read), and the
/// outcome, <c>accepted</c> or the defect's name. It travels as a JSON string in the response's additional properties
/// under <see cref="PropertyName"/>, so the report stores it without a type of its own.
/// </summary>
public sealed record TestDraftTranscript(int Requested, IReadOnlyList<int> Quotas, TestDraftAnswer? Answer, string Outcome)
{
    public const string PropertyName = "pharma.test-draft";

    public const string AcceptedOutcome = "accepted";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    public static TestDraftTranscript? FromJson(string json) => JsonSerializer.Deserialize<TestDraftTranscript>(json, Json);
}
