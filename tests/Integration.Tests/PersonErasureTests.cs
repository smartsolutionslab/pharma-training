using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Npgsql;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// A person's erasure end to end (#115), grown slice by slice; slice 1 covers Organisation, slice 2 Compliance, slice 3
/// Editorial and Learning. The person is a fresh id,
/// named and addressed by a fresh Guid and planted in Organisation's tables by hand, never a shared local user. Support
/// requests the erasure; Organisation refuses the last active manager of a pharmacy, otherwise ends the person's
/// memberships, revokes the invitations to their address, mails the confirmation once and then forgets their name and
/// address, and records its step in the erasure log. Compliance, which holds copies of the person's memberships
/// (planted by delivering Organisation's membership events), forgets the name there and in the messages it stored, and
/// its step follows. Editorial forgets the name beside the questions the person approved and the versions they returned
/// (planted by hand on versions the API made), keeping the reasons; Learning, whose copies hold the id only, purges the
/// membership messages it stored. Each announces its step; the steps after Organisation's arrive in any order.
/// </summary>
public class PersonErasureTests(AspireFixture aspire)
{
    private const string LastManager = "organisation.erasure-refused-last-manager";
    private const string StartedType = "SmartSolutionsLab.PharmaTraining.Contracts.Organisation.MembershipStartedV1";
    private const string ChangedType = "SmartSolutionsLab.PharmaTraining.Contracts.Organisation.MembershipChangedV1";
    private const string EditorialReason = "Kapitel 2 braucht eine Quelle.";

    private static readonly string[] AllSteps = ["organisation", "compliance", "editorial", "learning"];

    private static readonly Uri Erasures = new("/organisation/erasures", UriKind.Relative);
    private static readonly Uri Pharmacies = new("/organisation/pharmacies", UriKind.Relative);
    private static readonly TimeSpan StepWait = TimeSpan.FromSeconds(30);

    private readonly OrganisationDatabase database = new(aspire);
    private readonly Mailpit mailpit = new(aspire);

    [Fact]
    public async Task The_last_active_manager_is_refused_and_nothing_changes()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var probe = Probe.New();
        using var owner = await aspire.CreateHostClientAsync("owner");
        using var support = await aspire.CreateHostClientAsync("support");
        var pharmacy = await RegisterAsync(owner, cancellationToken);
        var membership = await database.InsertMembershipOfPersonAsync(pharmacy, probe.Person, probe.Address, "Active", cancellationToken, probe.Name, "Manager");
        var ownerSubject = await database.SubjectOfAsync("owner");
        var ownersMembership = (await database.ActiveMembershipsOfAsync(ownerSubject, cancellationToken)).Single(active => active.PharmacyId == pharmacy);
        await database.EndMembershipByHandAsync(ownersMembership.Id, cancellationToken);

        using var refused = await support.PostAsJsonAsync(Erasures, new { personId = probe.Person }, cancellationToken);

        var body = await refused.Content.ReadAsStringAsync(cancellationToken);
        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict, body);
        using var problem = JsonDocument.Parse(body);
        problem.RootElement.GetProperty("code").GetString().ShouldBe(LastManager);
        problem.RootElement.GetProperty("title").GetString().ShouldBe(ErrorCodeCatalog.MessageOf(LastManager));
        var member = await database.MemberAsync(membership, cancellationToken);
        member.Status.ShouldBe("Active");
        member.Name.ShouldBe(probe.Name);
        (await database.EmailOfMembershipAsync(membership, cancellationToken)).ShouldBe(probe.Address);
        (await mailpit.CountToAsync(probe.Address, cancellationToken)).ShouldBe(0);
    }

    [Fact]
    public async Task An_employee_is_erased_in_organisation()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var probe = Probe.New();
        using var owner = await aspire.CreateHostClientAsync("owner");
        using var support = await aspire.CreateHostClientAsync("support");
        var employer = await RegisterAsync(owner, cancellationToken);
        var membership = await database.InsertMembershipOfPersonAsync(employer, probe.Person, probe.Address, "Active", cancellationToken, probe.Name);
        var inviting = await RegisterAsync(owner, cancellationToken);
        var invitation = await database.InsertInvitationAsync(inviting, probe.Address, Hash(), "Invited", cancellationToken);

        var erasure = await RequestAsync(support, probe.Person, cancellationToken);

        var mails = await mailpit.MessagesToAsync(probe.Address, 1, cancellationToken);
        var confirmation = mails.ShouldHaveSingleItem();
        confirmation.Text.ShouldNotContain(probe.Name);
        confirmation.Text.ShouldNotContain(probe.Person.ToString());
        var read = await WaitForStepsAsync(support, erasure, AllSteps, cancellationToken);
        read.ConfirmationMailed.ShouldBeTrue();
        read.ConfirmationHandledAt.ShouldNotBeNull();
        var ended = await database.MemberAsync(membership, cancellationToken);
        ended.Status.ShouldBe("Ended");
        ended.Person.ShouldBe(probe.Person, "the PersonId stays behind as the pseudonym");
        ended.Name.ShouldBeNull();
        (await database.EmailOfMembershipAsync(membership, cancellationToken)).ShouldBeNull();
        var revoked = await database.MemberAsync(invitation, cancellationToken);
        revoked.Status.ShouldBe("Revoked");
        revoked.TokenHashes.ShouldBeEmpty();
        (await database.EmailOfMembershipAsync(invitation, cancellationToken)).ShouldBeNull();
        (await database.MembershipIdsWithEmailAsync(probe.Address, cancellationToken)).ShouldBeEmpty();
        (await mailpit.CountToAsync(probe.Address, cancellationToken)).ShouldBe(1, "the confirmation is mailed once");
    }

    [Fact]
    public async Task No_organisation_table_or_message_holds_the_persons_name_or_address()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var probe = Probe.New();
        using var owner = await aspire.CreateHostClientAsync("owner");
        using var support = await aspire.CreateHostClientAsync("support");
        var employer = await RegisterAsync(owner, cancellationToken);
        await database.InsertMembershipOfPersonAsync(employer, probe.Person, probe.Address, "Active", cancellationToken, probe.Name);
        var inviting = await RegisterAsync(owner, cancellationToken);
        await database.InsertInvitationAsync(inviting, probe.Address, Hash(), "Invited", cancellationToken);
        var sweep = new PersonalDataSweep(aspire, "organisation-database", "organisation", "wolverine");
        (await sweep.ColumnsHoldingAsync(probe.Name, cancellationToken)).ShouldContain("organisation.memberships.name", "the probe was not planted where the sweep looks");

        var erasure = await RequestAsync(support, probe.Person, cancellationToken);
        await WaitForStepsAsync(support, erasure, AllSteps, cancellationToken);

        (await sweep.ColumnsHoldingAsync(probe.Name, cancellationToken)).ShouldBeEmpty("a column still holds the erased person's name");
        (await sweep.ColumnsHoldingAsync(probe.Address, cancellationToken)).ShouldBeEmpty("a column still holds the erased person's address");
        (await sweep.ColumnsHoldingAsync(probe.Person.ToString(), cancellationToken))
            .Where(column => column.StartsWith("organisation.erasure", StringComparison.Ordinal))
            .ShouldBeEmpty("the erasure log holds the erased person's id");
    }

    [Fact]
    public async Task An_employee_is_erased_in_compliance()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var probe = Probe.New();
        using var owner = await aspire.CreateHostClientAsync("owner");
        using var support = await aspire.CreateHostClientAsync("support");
        var employer = await RegisterAsync(owner, cancellationToken);
        var membership = await database.InsertMembershipOfPersonAsync(employer, probe.Person, probe.Address, "Active", cancellationToken, probe.Name);
        var started = Started(membership, probe, employer);
        await ComplianceDatabase.DeliverAsync(aspire, StartedType, started, cancellationToken);
        await ComplianceDatabase.DeliverAsync(aspire, ChangedType, Changed(started, 2), cancellationToken);
        (await ComplianceNamesAsync(probe.Person, cancellationToken)).ShouldBe([probe.Name, probe.Name], "the probe was not planted in Compliance");

        var erasure = await RequestAsync(support, probe.Person, cancellationToken);
        await WaitForStepsAsync(support, erasure, AllSteps, cancellationToken);

        (await ComplianceNamesAsync(probe.Person, cancellationToken)).ShouldBe([null, null], "Compliance still holds the erased person's name");
        (await ComplianceDatabase.CountAsync(aspire, "select count(*) from compliance.erased_persons where person_id = @id", probe.Person, cancellationToken)).ShouldBe(1);
    }

    [Fact]
    public async Task No_compliance_table_or_message_holds_the_persons_name()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var probe = Probe.New();
        using var owner = await aspire.CreateHostClientAsync("owner");
        using var support = await aspire.CreateHostClientAsync("support");
        var employer = await RegisterAsync(owner, cancellationToken);
        var membership = await database.InsertMembershipOfPersonAsync(employer, probe.Person, probe.Address, "Active", cancellationToken, probe.Name);
        var started = Started(membership, probe, employer);
        await ComplianceDatabase.DeliverAsync(aspire, StartedType, started, cancellationToken);
        await ComplianceDatabase.DeliverAsync(aspire, ChangedType, Changed(started, 2), cancellationToken);
        var sweep = new PersonalDataSweep(aspire, "compliance-database", "compliance", "wolverine");
        var planted = await sweep.ColumnsHoldingAsync(probe.Name, cancellationToken);
        planted.ShouldContain("compliance.members.display_name", "the probe was not planted where the sweep looks");
        planted.ShouldContain("wolverine.wolverine_incoming_envelopes.body", "the delivered envelopes were not kept where the sweep looks");

        var erasure = await RequestAsync(support, probe.Person, cancellationToken);
        await WaitForStepsAsync(support, erasure, AllSteps, cancellationToken);

        (await sweep.ColumnsHoldingAsync(probe.Name, cancellationToken)).ShouldBeEmpty("a column still holds the erased person's name");
        (await sweep.ColumnsHoldingAsync(probe.Address, cancellationToken)).ShouldBeEmpty("a column still holds the erased person's address");
    }

    [Fact]
    public async Task A_membership_event_delivered_again_after_the_erasure_writes_no_name_in_compliance()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var probe = Probe.New();
        using var owner = await aspire.CreateHostClientAsync("owner");
        using var support = await aspire.CreateHostClientAsync("support");
        var employer = await RegisterAsync(owner, cancellationToken);
        var membership = await database.InsertMembershipOfPersonAsync(employer, probe.Person, probe.Address, "Active", cancellationToken, probe.Name);
        var started = Started(membership, probe, employer);
        await ComplianceDatabase.DeliverAsync(aspire, StartedType, started, cancellationToken);
        var erasure = await RequestAsync(support, probe.Person, cancellationToken);
        await WaitForStepsAsync(support, erasure, AllSteps, cancellationToken);

        // The same membership's start and a change again, and a start of another membership of the person, each under a
        // new envelope id so the inbox lets it through to the consumer.
        await ComplianceDatabase.DeliverAsync(aspire, StartedType, started with { Metadata = NewMetadata() }, cancellationToken);
        await ComplianceDatabase.DeliverAsync(aspire, ChangedType, Changed(started, 3), cancellationToken);
        var another = Started(Guid.CreateVersion7(), probe, employer);
        await ComplianceDatabase.DeliverAsync(aspire, StartedType, another, cancellationToken);

        (await new PersonalDataSweep(aspire, "compliance-database", "compliance").ColumnsHoldingAsync(probe.Name, cancellationToken))
            .ShouldBeEmpty("a redelivered membership event wrote the erased person's name back");
        (await ComplianceDatabase.CountAsync(aspire, "select count(*) from compliance.members where id = @id", another.MembershipId, cancellationToken)).ShouldBe(0);
    }

    [Fact]
    public async Task An_approver_and_returner_is_erased_in_editorial_and_the_return_reason_kept()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var probe = Probe.New();
        using var support = await aspire.CreateHostClientAsync("support");
        var connectionString = await EditorialDatabase.ConnectionStringAsync(aspire, cancellationToken);
        var draft = await EditorialReturnedAsync(probe, connectionString, cancellationToken);
        (await EditorialReviewers.ApprovalOfAsync(connectionString, draft.Question, cancellationToken)).ShouldBe((probe.Person, probe.Name), "the probe was not planted in Editorial");
        var rowVersion = await EditorialReviewers.RowVersionOfAsync(connectionString, draft.Id, cancellationToken);

        var erasure = await RequestAsync(support, probe.Person, cancellationToken);
        await WaitForStepsAsync(support, erasure, AllSteps, cancellationToken);

        (await EditorialReviewers.ApprovalOfAsync(connectionString, draft.Question, cancellationToken)).ShouldBe((probe.Person, null), "Editorial still holds the approver's name");
        (await EditorialReviewers.ReturnOfAsync(connectionString, draft.Id, cancellationToken)).ShouldBe((probe.Person, null, EditorialReason), "the return keeps its person and reason, not the name");
        (await EditorialReviewers.RowVersionOfAsync(connectionString, draft.Id, cancellationToken)).ShouldBe(rowVersion, "forgetting a name is no change of the course version");
    }

    [Fact]
    public async Task No_editorial_table_or_message_holds_the_persons_name()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var probe = Probe.New();
        using var support = await aspire.CreateHostClientAsync("support");
        var connectionString = await EditorialDatabase.ConnectionStringAsync(aspire, cancellationToken);
        await EditorialReturnedAsync(probe, connectionString, cancellationToken);
        var sweep = new PersonalDataSweep(aspire, "editorial-database", "editorial", "wolverine");
        var planted = await sweep.ColumnsHoldingAsync(probe.Name, cancellationToken);
        planted.ShouldContain("editorial.course_version_questions.reviewed_by_name", "the probe was not planted where the sweep looks");
        planted.ShouldContain("editorial.course_version_returns.returned_by_name", "the probe was not planted where the sweep looks");

        var erasure = await RequestAsync(support, probe.Person, cancellationToken);
        await WaitForStepsAsync(support, erasure, AllSteps, cancellationToken);

        (await sweep.ColumnsHoldingAsync(probe.Name, cancellationToken)).ShouldBeEmpty("a column still holds the erased person's name");
    }

    [Fact]
    public async Task No_learning_table_or_message_holds_the_persons_name()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var probe = Probe.New();
        using var owner = await aspire.CreateHostClientAsync("owner");
        using var support = await aspire.CreateHostClientAsync("support");
        var employer = await RegisterAsync(owner, cancellationToken);
        var membership = await database.InsertMembershipOfPersonAsync(employer, probe.Person, probe.Address, "Active", cancellationToken, probe.Name);
        var started = Started(membership, probe, employer);
        await LearningDatabase.DeliverAsync(aspire, StartedType, started, cancellationToken);
        await LearningDatabase.DeliverAsync(aspire, ChangedType, Changed(started, 2), cancellationToken);
        var sweep = new PersonalDataSweep(aspire, "learning-database", "learning", "wolverine");
        (await sweep.ColumnsHoldingAsync(probe.Name, cancellationToken))
            .ShouldContain("wolverine.wolverine_incoming_envelopes.body", "the delivered envelopes were not kept where the sweep looks");

        var erasure = await RequestAsync(support, probe.Person, cancellationToken);
        await WaitForStepsAsync(support, erasure, AllSteps, cancellationToken);

        (await sweep.ColumnsHoldingAsync(probe.Name, cancellationToken)).ShouldBeEmpty("a column still holds the erased person's name");
        (await sweep.ColumnsHoldingAsync(probe.Address, cancellationToken)).ShouldBeEmpty("a column still holds the erased person's address");
        (await LearningDatabase.CountAsync(aspire, "select count(*) from learning.members where person_id = @id", probe.Person, cancellationToken))
            .ShouldBe(1, "Learning keeps the membership under the PersonId, the pseudonym");
    }

    /// <summary>
    /// The purge finds the name in a body Wolverine serialised itself, not only in the JSON a test plants: Organisation
    /// publishes the change of the person's membership, and Compliance and Learning store what they received.
    /// </summary>
    [Fact]
    public async Task A_membership_change_organisation_sent_is_purged_from_compliance_and_learning()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var probe = Probe.New();
        using var owner = await aspire.CreateHostClientAsync("owner");
        using var support = await aspire.CreateHostClientAsync("support");
        var employer = await RegisterAsync(owner, cancellationToken);
        var membership = await database.InsertMembershipOfPersonAsync(employer, probe.Person, probe.Address, "Active", cancellationToken, probe.Name);
        using (var changed = await owner.PatchAsJsonAsync(new Uri($"/organisation/pharmacies/{employer}/members/{membership}", UriKind.Relative), new { jobFunction = "Pka", role = "Employee" }, cancellationToken))
        {
            changed.StatusCode.ShouldBe(HttpStatusCode.OK, await changed.Content.ReadAsStringAsync(cancellationToken));
        }

        await using (var compliance = await ComplianceDatabase.OpenAsync(aspire, cancellationToken))
        {
            await WaitForHandledBodyAsync(compliance, "Compliance", probe.Name, cancellationToken);
        }

        await using (var learning = await LearningDatabase.OpenAsync(aspire, cancellationToken))
        {
            await WaitForHandledBodyAsync(learning, "Learning", probe.Name, cancellationToken);
        }

        var erasure = await RequestAsync(support, probe.Person, cancellationToken);
        await WaitForStepsAsync(support, erasure, AllSteps, cancellationToken);

        (await new PersonalDataSweep(aspire, "compliance-database", "wolverine").ColumnsHoldingAsync(probe.Name, cancellationToken))
            .ShouldBeEmpty("Compliance still stores the change Organisation sent with the erased person's name");
        (await new PersonalDataSweep(aspire, "learning-database", "wolverine").ColumnsHoldingAsync(probe.Name, cancellationToken))
            .ShouldBeEmpty("Learning still stores the change Organisation sent with the erased person's name");
    }

    [Fact]
    public async Task An_empty_person_id_is_a_bad_request()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var support = await aspire.CreateHostClientAsync("support");

        using var refused = await support.PostAsJsonAsync(Erasures, new { personId = Guid.Empty }, cancellationToken);

        var body = await refused.Content.ReadAsStringAsync(cancellationToken);
        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest, body);
        using var problem = JsonDocument.Parse(body);
        problem.RootElement.GetProperty("code").GetString().ShouldBe("invalid-argument");
    }

    private static async Task<Guid> RegisterAsync(HttpClient client, CancellationToken cancellationToken)
    {
        using var created = await client.PostAsJsonAsync(Pharmacies, new { name = $"Apotheke {Guid.CreateVersion7()}", jobFunction = "Pharmacist" }, cancellationToken);
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        return await created.Content.ReadFromJsonAsync<Guid>(cancellationToken);
    }

    private static async Task<Uri> RequestAsync(HttpClient support, Guid person, CancellationToken cancellationToken)
    {
        using var created = await support.PostAsJsonAsync(Erasures, new { personId = person }, cancellationToken);
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync(cancellationToken));
        return created.Headers.Location.ShouldNotBeNull();
    }

    /// <summary>Reads the erasure until its steps are <paramref name="modules"/>, the first first and the rest in any order, for at most <see cref="StepWait"/>; fails naming the last read.</summary>
    private static async Task<ErasureRead> WaitForStepsAsync(HttpClient support, Uri erasure, string[] modules, CancellationToken cancellationToken)
    {
        using var expired = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        expired.CancelAfter(StepWait);
        ErasureRead? read = null;
        while (true)
        {
            using var response = await support.GetAsync(erasure, cancellationToken);
            response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(cancellationToken));
            read = await response.Content.ReadFromJsonAsync<ErasureRead>(cancellationToken);
            if (read is not null && read.Steps.Count > 0 && read.Steps[0].Module == modules[0] && read.Steps.Select(step => step.Module).Order(StringComparer.Ordinal).SequenceEqual(modules.Order(StringComparer.Ordinal)))
            {
                return read;
            }

            if (expired.IsCancellationRequested)
            {
                break;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
        }

        read.ShouldNotBeNull();
        read.Steps.Select(step => step.Module).ShouldBe(modules, ignoreOrder: true, $"the erasure's steps within {StepWait.TotalSeconds:0} s");
        read.Steps[0].Module.ShouldBe(modules[0], "Organisation records its own step first");
        return read;
    }

    private static StartedEvent Started(Guid membership, Probe probe, Guid pharmacy) =>
        new(membership, probe.Person, pharmacy, probe.Name, "Pta", "Employee", TimeProvider.System.GetUtcNow(), NewMetadata());

    private static ChangedEvent Changed(StartedEvent started, int version) =>
        new(started.MembershipId, started.PersonId, started.PharmacyId, started.DisplayName, "Pka", "Employee", version, NewMetadata());

    private static EventMetadata NewMetadata() => new(Guid.CreateVersion7(), TimeProvider.System.GetUtcNow());

    /// <summary>The display names Compliance holds for the person: on their members, then on their membership changes.</summary>
    private async Task<List<string?>> ComplianceNamesAsync(Guid person, CancellationToken cancellationToken)
    {
        await using var connection = await ComplianceDatabase.OpenAsync(aspire, cancellationToken);
        await using var command = new NpgsqlCommand(
            "select display_name from compliance.members where person_id = @id union all select display_name from compliance.membership_changes where person_id = @id",
            connection);
        command.Parameters.AddWithValue("id", person);
        List<string?> names = [];
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            names.Add(await reader.IsDBNullAsync(0, cancellationToken) ? null : reader.GetString(0));
        }

        return names;
    }

    /// <summary>
    /// A new course's first version, made, submitted and returned by the editor through the API, with the probe planted
    /// as its question's approver and its return's returner.
    /// </summary>
    private async Task<EditorialWorkflow.WorkflowDraft> EditorialReturnedAsync(Probe probe, string connectionString, CancellationToken cancellationToken)
    {
        using var editor = await aspire.CreateHostClientAsync("editor");
        var draft = await EditorialWorkflow.ReadyToSubmitAsync(editor, cancellationToken);
        await EditorialWorkflow.SubmitAsync(editor, draft.Id, cancellationToken);
        await EditorialWorkflow.ReturnAsync(editor, draft.Id, EditorialReason, cancellationToken);
        await EditorialReviewers.AttributeAsync(connectionString, draft.Id, draft.Question, probe.Person, probe.Name, cancellationToken);
        return draft;
    }

    /// <summary>Waits until the service's durable inbox holds a handled membership change whose body holds the needle.</summary>
    private static async Task WaitForHandledBodyAsync(NpgsqlConnection connection, string service, string needle, CancellationToken cancellationToken)
    {
        var started = TimeProvider.System.GetTimestamp();
        while (true)
        {
            await using var command = new NpgsqlCommand(
                "select exists (select 1 from wolverine.wolverine_incoming_envelopes where status = 'Handled' and message_type = @type and position(convert_to(@needle, 'UTF8') in body) > 0)",
                connection);
            command.Parameters.AddWithValue("type", ChangedType);
            command.Parameters.AddWithValue("needle", needle);
            if (await command.ExecuteScalarAsync(cancellationToken) is true)
            {
                return;
            }

            if (TimeProvider.System.GetElapsedTime(started) >= StepWait)
            {
                throw new TimeoutException($"{service}'s inbox held no handled membership change with the probe's name after {StepWait.TotalSeconds} s.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
        }
    }

    private static string Hash() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));

    /// <summary>The person to erase: a fresh id, a name and an address only this test uses.</summary>
    private sealed record Probe(Guid Person, string Name, string Address)
    {
        public static Probe New()
        {
            var id = Guid.CreateVersion7();
            return new Probe(id, $"Erasure Probe {id:N}", $"erasure-{id:N}@example.com");
        }
    }

    private sealed record ErasureRead(Guid Id, DateTimeOffset RequestedAt, Guid RequestedBy, bool ConfirmationMailed, DateTimeOffset? ConfirmationHandledAt, IReadOnlyList<ErasureStepRead> Steps);

    private sealed record ErasureStepRead(string Module, DateTimeOffset CompletedAt);

    private sealed record EventMetadata(Guid EventIdentifier, DateTimeOffset OccurredAt);

    private sealed record StartedEvent(Guid MembershipId, Guid PersonId, Guid PharmacyId, string DisplayName, string JobFunction, string Role, DateTimeOffset StartedAt, EventMetadata Metadata);

    private sealed record ChangedEvent(Guid MembershipId, Guid PersonId, Guid PharmacyId, string DisplayName, string JobFunction, string Role, int Version, EventMetadata Metadata);
}
