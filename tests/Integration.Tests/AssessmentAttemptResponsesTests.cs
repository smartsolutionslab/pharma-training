using System.Net;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using static SmartSolutionsLab.PharmaTraining.Integration.Tests.AssessmentAttempts;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// The issue's first done-when at run time: no response of an attempt carries correctness. Every JSON body Assessment
/// answers on the attempt routes, through a failed and a passed attempt, is walked: no key names correctness, an
/// option is its id and text only, and a question is its id, type, text, options and the learner's chosen options.
/// </summary>
public partial class AssessmentAttemptResponsesTests(AspireFixture aspire)
{
    private static readonly string[] OptionKeys = ["id", "text"];
    private static readonly string[] QuestionKeys = ["id", "type", "text", "options", "chosenOptionIds"];

    [Fact]
    public async Task No_response_of_an_attempt_names_correctness()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("employee-a");
        var opened = await OpenExaminationAsync(aspire, client, cancellationToken);
        var single = opened.Definition.Questions[0];
        var multiple = opened.Definition.Questions[1];
        List<string> bodies = [];

        await RecordAsync(client.GetAsync(BySession(opened.Completed.SessionId), cancellationToken), HttpStatusCode.OK);
        await RecordAsync(StartAsync(client, opened.ExaminationId, cancellationToken), HttpStatusCode.Created);
        await RecordAsync(client.GetAsync(BySession(opened.Completed.SessionId), cancellationToken), HttpStatusCode.OK);
        await RecordAsync(AnswerAsync(client, opened.ExaminationId, single.QuestionId, Wrong(single), cancellationToken), HttpStatusCode.NoContent);
        await RecordAsync(AnswerAsync(client, opened.ExaminationId, multiple.QuestionId, Right(multiple), cancellationToken), HttpStatusCode.NoContent);
        await RecordAsync(client.GetAsync(BySession(opened.Completed.SessionId), cancellationToken), HttpStatusCode.OK);
        await RecordAsync(SubmitAsync(client, opened.ExaminationId, cancellationToken), HttpStatusCode.OK);
        await RecordAsync(client.GetAsync(BySession(opened.Completed.SessionId), cancellationToken), HttpStatusCode.OK);
        await RecordAsync(StartAsync(client, opened.ExaminationId, cancellationToken), HttpStatusCode.Created);
        await RecordAsync(AnswerAsync(client, opened.ExaminationId, single.QuestionId, Right(single), cancellationToken), HttpStatusCode.NoContent);
        await RecordAsync(AnswerAsync(client, opened.ExaminationId, multiple.QuestionId, Right(multiple), cancellationToken), HttpStatusCode.NoContent);
        await RecordAsync(client.GetAsync(BySession(opened.Completed.SessionId), cancellationToken), HttpStatusCode.OK);
        await RecordAsync(SubmitAsync(client, opened.ExaminationId, cancellationToken), HttpStatusCode.OK);
        await RecordAsync(client.GetAsync(BySession(opened.Completed.SessionId), cancellationToken), HttpStatusCode.OK);

        List<string> violations = [];
        var options = 0;
        var questions = 0;
        foreach (var body in bodies)
        {
            Walk(JsonNode.Parse(body), "$", parentKey: null, violations, ref options, ref questions);
        }

        violations.ShouldBeEmpty();
        bodies.Count.ShouldBe(9);
        questions.ShouldBeGreaterThan(0, "the bodies showed no question");
        options.ShouldBeGreaterThan(0, "the bodies showed no option");

        async Task RecordAsync(Task<HttpResponseMessage> sending, HttpStatusCode expected)
        {
            using var response = await sending;
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            response.StatusCode.ShouldBe(expected, $"{response.RequestMessage?.Method} {response.RequestMessage?.RequestUri}: {body}");
            if (body.Length > 0)
            {
                bodies.Add(body);
            }
        }
    }

    private static void Walk(JsonNode? node, string path, string? parentKey, List<string> violations, ref int options, ref int questions)
    {
        switch (node)
        {
            case JsonObject item:
                var keys = item.Select(property => property.Key).ToList();
                if (parentKey == "options")
                {
                    options++;
                    if (!keys.Order(StringComparer.Ordinal).SequenceEqual(OptionKeys.Order(StringComparer.Ordinal)))
                    {
                        violations.Add($"{path} is an option with the keys {string.Join(", ", keys)}");
                    }
                }

                if (parentKey == "questions")
                {
                    questions++;
                    if (!keys.Order(StringComparer.Ordinal).SequenceEqual(QuestionKeys.Order(StringComparer.Ordinal)))
                    {
                        violations.Add($"{path} is a question with the keys {string.Join(", ", keys)}");
                    }
                }

                foreach (var (key, value) in item)
                {
                    if (Correctness().IsMatch(key))
                    {
                        violations.Add($"{path}.{key} names correctness");
                    }

                    Walk(value, $"{path}.{key}", key, violations, ref options, ref questions);
                }

                break;
            case JsonArray array:
                for (var index = 0; index < array.Count; index++)
                {
                    Walk(array[index], $"{path}[{index}]", parentKey, violations, ref options, ref questions);
                }

                break;
        }
    }

    [GeneratedRegex("correct|solution|answerkey", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Correctness();
}
