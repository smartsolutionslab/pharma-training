using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Aspire.Hosting.Testing;
using static SmartSolutionsLab.PharmaTraining.Integration.Tests.EditorialWorkflow;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// Four eyes binds the editor who last changed a version's test blueprint (ADR 0007, #246). The run uses four eyes: the
/// editor drafts a version and approves its question, editor-b changes the blueprint, which resets the approval, and the
/// editor approves again and submits. Editor-b neither submitted nor approved, but may not publish; editor-c may.
/// </summary>
[Collection(EditorialDrafts.Name)]
public class EditorialFourEyesTests(AspireFixture aspire)
{
    private const string FourEyes = "editorial.four-eyes";

    [Fact]
    public async Task The_editor_who_last_changed_the_test_blueprint_cannot_publish()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var editor = await aspire.CreateHostClientAsync("editor");
        using var changer = await aspire.CreateHostClientAsync("editor-b");
        var id = await SubmittedAfterABlueprintChangeAsync(editor, changer, cancellationToken);

        using var refused = await StepAsync(changer, id, "publish", cancellationToken);

        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden, await refused.Content.ReadAsStringAsync(cancellationToken));
        using var problem = JsonDocument.Parse(await refused.Content.ReadAsStringAsync(cancellationToken));
        problem.RootElement.GetProperty("code").GetString().ShouldBe(FourEyes);
        problem.RootElement.GetProperty("title").GetString().ShouldBe(ErrorCodeCatalog.MessageOf(FourEyes));
        (await ReadAsync(editor, id, cancellationToken)).Status.ShouldBe("InReview");
    }

    [Fact]
    public async Task A_third_editor_publishes_after_a_change_of_the_test_blueprint()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var editor = await aspire.CreateHostClientAsync("editor");
        using var changer = await aspire.CreateHostClientAsync("editor-b");
        using var publisher = await aspire.CreateHostClientAsync(Publisher);
        var id = await SubmittedAfterABlueprintChangeAsync(editor, changer, cancellationToken);

        using var published = await StepAsync(publisher, id, "publish", cancellationToken);

        published.StatusCode.ShouldBe(HttpStatusCode.OK, await published.Content.ReadAsStringAsync(cancellationToken));
        (await ReadAsync(editor, id, cancellationToken)).Status.ShouldBe("Published");
    }

    /// <summary>
    /// A version the editor drafted and approved, whose passing score <paramref name="changer"/> then changed (the same
    /// number of questions), and which the editor approved again and submitted: its id.
    /// </summary>
    private static async Task<Guid> SubmittedAfterABlueprintChangeAsync(HttpClient editor, HttpClient changer, CancellationToken cancellationToken)
    {
        var draft = await ReadyToSubmitAsync(editor, cancellationToken);
        await ApproveAllAsync(editor, draft.Id, cancellationToken);

        var blueprint = new Uri($"/editorial/course-versions/{draft.Id}/blueprint", UriKind.Relative);
        using (var changed = await changer.PutAsJsonAsync(blueprint, new { questions = 1, passingScorePercent = 90, validityMonths = 12 }, cancellationToken))
        {
            changed.StatusCode.ShouldBe(HttpStatusCode.OK, $"PUT {blueprint} answered {(int)changed.StatusCode} {await changed.Content.ReadAsStringAsync(cancellationToken)}");
        }

        (await ReadAsync(editor, draft.Id, cancellationToken)).Questions.ShouldAllBe(question => question.Review == "Unreviewed");
        await ApproveAllAsync(editor, draft.Id, cancellationToken);
        await SubmitAsync(editor, draft.Id, cancellationToken);
        return draft.Id;
    }
}
