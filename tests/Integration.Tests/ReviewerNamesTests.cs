using Microsoft.EntityFrameworkCore;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Persistence;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Infrastructure;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Infrastructure.Persistence;
using static SmartSolutionsLab.PharmaTraining.Integration.Tests.EditorialWorkflow;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// Editorial's store forgets a person's name (#115, spec 4.4) against its real tables: <c>reviewed_by_name</c> on the
/// questions the person approved and <c>returned_by_name</c> on the returns they made become NULL, while the person's id,
/// the approval's time and the return's reason stay, and everyone else's names stay too. It is a data-protection update of
/// historical rows, not a change of a course version: the version's row version does not move. The approver and the
/// returner are planted by hand on versions the API made, a fresh id each, never a shared local user.
/// </summary>
public class ReviewerNamesTests(AspireFixture aspire)
{
    [Fact]
    public async Task The_persons_names_are_forgotten_on_their_approvals_and_returns_and_everyone_elses_are_kept()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var editor = await aspire.CreateHostClientAsync("editor");
        var connectionString = await EditorialDatabase.ConnectionStringAsync(aspire, cancellationToken);
        var person = Guid.CreateVersion7();
        var someoneElse = Guid.CreateVersion7();
        var theirs = await ReturnedAsync(editor, "Quelle fehlt.", cancellationToken);
        var others = await ReturnedAsync(editor, "Bild fehlt.", cancellationToken);
        await EditorialReviewers.AttributeAsync(connectionString, theirs.Id, theirs.Question, person, $"Reviewer {person:N}", cancellationToken);
        await EditorialReviewers.AttributeAsync(connectionString, others.Id, others.Question, someoneElse, $"Reviewer {someoneElse:N}", cancellationToken);
        var rowVersion = await EditorialReviewers.RowVersionOfAsync(connectionString, theirs.Id, cancellationToken);

        await ForgetAsync(connectionString, person, cancellationToken);

        (await EditorialReviewers.ApprovalOfAsync(connectionString, theirs.Question, cancellationToken)).ShouldBe((person, null));
        (await EditorialReviewers.ReturnOfAsync(connectionString, theirs.Id, cancellationToken)).ShouldBe((person, null, "Quelle fehlt."), "the PersonId and the reason stay");
        (await EditorialReviewers.ApprovalOfAsync(connectionString, others.Question, cancellationToken)).ShouldBe((someoneElse, $"Reviewer {someoneElse:N}"));
        (await EditorialReviewers.ReturnOfAsync(connectionString, others.Id, cancellationToken)).ShouldBe((someoneElse, $"Reviewer {someoneElse:N}", "Bild fehlt."));
        (await EditorialReviewers.RowVersionOfAsync(connectionString, theirs.Id, cancellationToken)).ShouldBe(rowVersion, "forgetting a name is no change of the course version");
    }

    [Fact]
    public async Task Forgetting_a_person_again_or_one_editorial_never_saw_changes_nothing_and_does_not_fail()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var editor = await aspire.CreateHostClientAsync("editor");
        var connectionString = await EditorialDatabase.ConnectionStringAsync(aspire, cancellationToken);
        var person = Guid.CreateVersion7();
        var theirs = await ReturnedAsync(editor, "Quelle fehlt.", cancellationToken);
        await EditorialReviewers.AttributeAsync(connectionString, theirs.Id, theirs.Question, person, $"Reviewer {person:N}", cancellationToken);
        await ForgetAsync(connectionString, person, cancellationToken);

        await ForgetAsync(connectionString, person, cancellationToken);
        await ForgetAsync(connectionString, Guid.CreateVersion7(), cancellationToken);

        (await EditorialReviewers.ApprovalOfAsync(connectionString, theirs.Question, cancellationToken)).ShouldBe((person, null));
        (await EditorialReviewers.ReturnOfAsync(connectionString, theirs.Id, cancellationToken)).ShouldBe((person, null, "Quelle fehlt."));
    }

    /// <summary>A new course's first version, submitted and returned to draft with the reason, by the editor.</summary>
    private static async Task<WorkflowDraft> ReturnedAsync(HttpClient editor, string reason, CancellationToken cancellationToken)
    {
        var draft = await ReadyToSubmitAsync(editor, cancellationToken);
        await SubmitAsync(editor, draft.Id, cancellationToken);
        await ReturnAsync(editor, draft.Id, reason, cancellationToken);
        return draft;
    }

    private static async Task ForgetAsync(string connectionString, Guid person, CancellationToken cancellationToken)
    {
        var options = new DbContextOptionsBuilder<EditorialDbContext>().UseNpgsql(connectionString).Options;
        await using var context = new EditorialDbContext(options, NoServices.Instance);
        await new ReviewerNames(context).ForgetAsync(PersonId.From(person), cancellationToken);
    }
}
