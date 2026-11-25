using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.Configuration;
using Npgsql;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Storage;

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
/// membership messages it stored. Certification (slice 4) removes the certificates no pharmacy keeps any longer, row and
/// PDF, keeps the retained ones with the name, forgets the name on its copies of the memberships and drops what arrives
/// for the person afterwards. Each announces its step; the steps after Organisation's arrive in any order. The class
/// calls the public verification, so it runs in that endpoint's collection (one per-address limit, docs/adr/0029).
/// </summary>
[Collection(CertificationVerification.Name)]
public class PersonErasureTests(AspireFixture aspire)
{
    private const string LastManager = "organisation.erasure-refused-last-manager";
    private const string StartedType = "SmartSolutionsLab.PharmaTraining.Contracts.Organisation.MembershipStartedV1";
    private const string ChangedType = "SmartSolutionsLab.PharmaTraining.Contracts.Organisation.MembershipChangedV1";
    private const string EditorialReason = "Kapitel 2 braucht eine Quelle.";

    private const string NotVerifiable = "certification.certificate-not-verifiable";
    private const string StoredOfHolder = "select count(*) from certification.certificates where holder_id = @id and document_state = 'Stored'";

    private static readonly string[] AllSteps = ["organisation", "compliance", "editorial", "learning", "certification"];

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

        var erasure = await RequestAsync(support, probe.Person, cancellationToken);
        await WaitForStepsAsync(support, erasure, AllSteps, cancellationToken);

        (await EditorialReviewers.ApprovalOfAsync(connectionString, draft.Question, cancellationToken)).ShouldBe((probe.Person, null), "Editorial still holds the approver's name");
        (await EditorialReviewers.ReturnOfAsync(connectionString, draft.Id, cancellationToken)).ShouldBe((probe.Person, null, EditorialReason), "the return keeps its person and reason, not the name");
    }

    [Fact]
    public async Task A_person_editorial_never_saw_is_tombstoned_in_editorial()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var probe = Probe.New();
        using var support = await aspire.CreateHostClientAsync("support");
        var connectionString = await EditorialDatabase.ConnectionStringAsync(aspire, cancellationToken);

        var erasure = await RequestAsync(support, probe.Person, cancellationToken);
        await WaitForStepsAsync(support, erasure, AllSteps, cancellationToken);

        (await EditorialReviewers.TombstonesOfAsync(connectionString, probe.Person, cancellationToken)).ShouldBe(1, "Editorial keeps no tombstone of the erased person");
    }

    /// <summary>
    /// The erased person's account and tokens outlive the erasure (until slice 6), and a next version drafted at the same
    /// time copies the names it loaded: a name written beside the person's approval after Editorial's step, planted here as
    /// such an approval writes it, must not be copied into the next version.
    /// </summary>
    [Fact]
    public async Task A_name_written_after_the_erasure_is_not_copied_into_the_next_version()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var probe = Probe.New();
        using var support = await aspire.CreateHostClientAsync("support");
        using var editor = await aspire.CreateHostClientAsync("editor");
        using var publisher = await aspire.CreateHostClientAsync(EditorialWorkflow.Publisher);
        var connectionString = await EditorialDatabase.ConnectionStringAsync(aspire, cancellationToken);
        var published = await EditorialWorkflow.PublishedAsync(editor, publisher, cancellationToken);
        var erasure = await RequestAsync(support, probe.Person, cancellationToken);
        await WaitForStepsAsync(support, erasure, AllSteps, cancellationToken);
        await EditorialReviewers.AttributeApprovalAsync(connectionString, published.Question, probe.Person, probe.Name, cancellationToken);

        var next = await EditorialWorkflow.DraftNextAsync(editor, published.Id, cancellationToken);

        (await EditorialReviewers.ApprovalsOfVersionAsync(connectionString, next, cancellationToken))
            .ShouldHaveSingleItem().ShouldBe((probe.Person, null), "the next version copied the erased person's name");
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

    /// <summary>
    /// #421 review: a membership start and change of the person that arrive after the erasure (in flight past its purge,
    /// redelivered or replayed) leave no stored message body with the name in Compliance or Learning, and Learning keeps
    /// no copy of the new membership.
    /// </summary>
    [Fact]
    public async Task A_membership_message_arriving_after_the_erasure_leaves_no_stored_body_with_the_name_in_compliance_or_learning()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var probe = Probe.New();
        using var owner = await aspire.CreateHostClientAsync("owner");
        using var support = await aspire.CreateHostClientAsync("support");
        var employer = await RegisterAsync(owner, cancellationToken);
        var membership = await database.InsertMembershipOfPersonAsync(employer, probe.Person, probe.Address, "Active", cancellationToken, probe.Name);
        var started = Started(membership, probe, employer);
        await ComplianceDatabase.DeliverAsync(aspire, StartedType, started, cancellationToken);
        await LearningDatabase.DeliverAsync(aspire, StartedType, started, cancellationToken);
        var erasure = await RequestAsync(support, probe.Person, cancellationToken);
        await WaitForStepsAsync(support, erasure, AllSteps, cancellationToken);

        var late = Started(Guid.CreateVersion7(), probe, employer);
        await ComplianceDatabase.DeliverAsync(aspire, StartedType, late, cancellationToken);
        await ComplianceDatabase.DeliverAsync(aspire, ChangedType, Changed(started, 3), cancellationToken);
        await LearningDatabase.DeliverAsync(aspire, StartedType, late, cancellationToken);
        await LearningDatabase.DeliverAsync(aspire, ChangedType, Changed(started, 3), cancellationToken);

        (await new PersonalDataSweep(aspire, "compliance-database", "compliance", "wolverine").ColumnsHoldingAsync(probe.Name, cancellationToken))
            .ShouldBeEmpty("Compliance still stores a membership message for the erased person with their name");
        (await new PersonalDataSweep(aspire, "learning-database", "learning", "wolverine").ColumnsHoldingAsync(probe.Name, cancellationToken))
            .ShouldBeEmpty("Learning still stores a membership message for the erased person with their name");
        (await LearningDatabase.CountAsync(aspire, "select count(*) from learning.members where id = @id", late.MembershipId, cancellationToken)).ShouldBe(0);
        (await LearningDatabase.CountAsync(aspire, "select count(*) from learning.erased_persons where person_id = @id", probe.Person, cancellationToken)).ShouldBe(1);
    }

    /// <summary>
    /// Case 4 (slice 4): the person has worked in a pharmacy for six years. A certificate passed five years and a day ago,
    /// and one passed seven years ago, before the membership, are in no pharmacy's view: their rows and PDFs are gone and
    /// their codes no longer verify. One passed a year ago is retained: it verifies with the name and is in the
    /// pharmacy's export (Q2). Certification's copy of the membership keeps its row without the name.
    /// </summary>
    [Fact]
    public async Task Certificates_no_pharmacy_keeps_are_removed_with_their_pdfs_and_a_retained_one_still_verifies_and_is_exported()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var probe = Probe.New();
        using var support = await aspire.CreateHostClientAsync("support");
        using var developer = await aspire.CreateHostClientAsync("developer");
        using var anonymous = aspire.CreateHostClient();
        var now = TimeProvider.System.GetUtcNow();
        var planted = await CertificationPlantedAsync(developer, probe, now, cancellationToken);
        var blobs = await CertificationBlobsAsync(cancellationToken);
        foreach (var certificate in planted.All)
        {
            (await blobs.FindAsync(CertificatePdf(certificate.Id), cancellationToken)).HasValue.ShouldBeTrue($"the PDF of {certificate.Id} was not planted");
        }

        var erasure = await RequestAsync(support, probe.Person, cancellationToken);
        await WaitForStepsAsync(support, erasure, AllSteps, cancellationToken);

        foreach (var removed in new[] { planted.Old, planted.Outside })
        {
            (await CertificationDatabase.CountAsync(aspire, "select count(*) from certification.certificates where id = @id", removed.Id, cancellationToken)).ShouldBe(0, $"the row of {removed.Id} is still there");
            (await blobs.FindAsync(CertificatePdf(removed.Id), cancellationToken)).HasValue.ShouldBeFalse($"the PDF of {removed.Id} is still there");
            using var refused = await anonymous.GetAsync(Verify(removed.VerificationCode), cancellationToken);
            var body = await refused.Content.ReadAsStringAsync(cancellationToken);
            refused.StatusCode.ShouldBe(HttpStatusCode.NotFound, body);
            using var problem = JsonDocument.Parse(body);
            problem.RootElement.GetProperty("code").GetString().ShouldBe(NotVerifiable);
        }

        (await blobs.FindAsync(CertificatePdf(planted.Retained.Id), cancellationToken)).HasValue.ShouldBeTrue("a retained certificate keeps its PDF");
        using (var verified = await anonymous.GetAsync(Verify(planted.Retained.VerificationCode), cancellationToken))
        {
            var body = await verified.Content.ReadAsStringAsync(cancellationToken);
            verified.StatusCode.ShouldBe(HttpStatusCode.OK, body);
            using var json = JsonDocument.Parse(body);
            json.RootElement.GetProperty("holderName").GetString().ShouldBe(probe.Name);
        }

        var exported = await ExportedIdsAsync(developer, planted.Pharmacy, now.AddYears(-1).AddDays(-3), now.AddYears(-1).AddDays(3), cancellationToken);
        exported.ShouldBe([planted.Retained.Id]);
        (await CertificationDatabase.CountAsync(aspire, "select count(*) from certification.memberships where person_id = @id and holder_name is null", probe.Person, cancellationToken))
            .ShouldBe(1, "the membership's row stays without the name");
        (await CertificationDatabase.CountAsync(aspire, "select count(*) from certification.erased_persons where person_id = @id", probe.Person, cancellationToken)).ShouldBe(1);
    }

    /// <summary>
    /// Case 3 (slice 4): after the erasure no column of Certification's schema or of its Wolverine tables holds the
    /// person's name, but the holder name of the retained certificate, and only that row.
    /// </summary>
    [Fact]
    public async Task No_certification_table_or_message_holds_the_persons_name_but_a_retained_certificate()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var probe = Probe.New();
        using var support = await aspire.CreateHostClientAsync("support");
        using var developer = await aspire.CreateHostClientAsync("developer");
        var planted = await CertificationPlantedAsync(developer, probe, TimeProvider.System.GetUtcNow(), cancellationToken);
        await CertificationDatabase.DeliverAsync(aspire, CertificationIssuing.ChangedType, CertificationIssuing.Changed(planted.Membership, "Employee", 2), cancellationToken);
        var sweep = new PersonalDataSweep(aspire, "certification-database", "certification", "wolverine");
        var before = await sweep.ColumnsHoldingAsync(probe.Name, cancellationToken);
        before.ShouldContain("certification.memberships.holder_name", "the probe was not planted where the sweep looks");
        before.ShouldContain("certification.certificates.holder_name", "the probe was not planted where the sweep looks");
        before.ShouldContain("wolverine.wolverine_incoming_envelopes.body", "the delivered envelopes were not kept where the sweep looks");

        var erasure = await RequestAsync(support, probe.Person, cancellationToken);
        await WaitForStepsAsync(support, erasure, AllSteps, cancellationToken);

        (await sweep.ColumnsHoldingAsync(probe.Name, cancellationToken)).ShouldBe(["certification.certificates.holder_name"], "only a retained certificate keeps the erased person's name");
        (await CertificateIdsNamedAsync(probe.Name, cancellationToken)).ShouldBe([planted.Retained.Id]);
    }

    /// <summary>
    /// Case 5 (slice 4): after the erasure a membership start of the person, delivered again or new, writes no name in
    /// Certification, and a passed test of theirs issues no certificate; both are handled, not left to retry.
    /// </summary>
    [Fact]
    public async Task A_membership_or_passed_test_delivered_after_the_erasure_writes_no_name_and_issues_no_certificate_in_certification()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var probe = Probe.New();
        using var support = await aspire.CreateHostClientAsync("support");
        var started = new CertificationIssuing.StartedEvent(Guid.CreateVersion7(), probe.Person, Guid.CreateVersion7(), probe.Name, "Pta", "Employee", TimeProvider.System.GetUtcNow().AddYears(-1), CertificationIssuing.NewMetadata());
        await CertificationDatabase.DeliverAsync(aspire, CertificationIssuing.StartedType, started, cancellationToken);
        var published = CertificationIssuing.Published();
        await CertificationDatabase.DeliverAsync(aspire, CertificationIssuing.PublishedType, published, cancellationToken);
        var erasure = await RequestAsync(support, probe.Person, cancellationToken);
        await WaitForStepsAsync(support, erasure, AllSteps, cancellationToken);

        await CertificationDatabase.DeliverAsync(aspire, CertificationIssuing.StartedType, started with { Metadata = CertificationIssuing.NewMetadata() }, cancellationToken);
        var another = started with { MembershipId = Guid.CreateVersion7(), PharmacyId = Guid.CreateVersion7(), Metadata = CertificationIssuing.NewMetadata() };
        await CertificationDatabase.DeliverAsync(aspire, CertificationIssuing.StartedType, another, cancellationToken);
        var passed = CertificationIssuing.Passed(started, published, TimeProvider.System.GetUtcNow());
        await CertificationDatabase.DeliverAsync(aspire, CertificationIssuing.PassedType, passed, cancellationToken);

        (await new PersonalDataSweep(aspire, "certification-database", "certification").ColumnsHoldingAsync(probe.Name, cancellationToken))
            .ShouldBeEmpty("a membership delivered after the erasure wrote the erased person's name back");
        (await CertificationDatabase.CountAsync(aspire, "select count(*) from certification.memberships where id = @id", another.MembershipId, cancellationToken)).ShouldBe(0);
        (await CertificationDatabase.CountAsync(aspire, "select count(*) from certification.certificates where attempt_id = @id", passed.AttemptId, cancellationToken)).ShouldBe(0);
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

    private static Uri Verify(string code) => new($"/certification/verify/{Uri.EscapeDataString(code)}", UriKind.Relative);

    private static BlobName CertificatePdf(Guid certificate) => BlobName.From($"certificates/{certificate:D}.pdf");

    private static string DayOf(DateTimeOffset instant) => instant.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// In Certification, through the events that reach it: the developer manages a fresh pharmacy from now on, and the
    /// probe has worked there under their name for six years. They passed a course version a year ago (retained), five
    /// years and a day ago (no longer kept) and seven years ago (before the membership); every PDF is stored.
    /// </summary>
    private async Task<CertificationPlanted> CertificationPlantedAsync(HttpClient developer, Probe probe, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var pharmacy = Guid.CreateVersion7();
        await CertificationDatabase.DeliverAsync(aspire, CertificationIssuing.StartedType, CertificationIssuing.StartedIn(LearningSessions.SubjectOf(developer), pharmacy, "Manager", now), cancellationToken);
        var membership = new CertificationIssuing.StartedEvent(Guid.CreateVersion7(), probe.Person, pharmacy, probe.Name, "Pta", "Employee", now.AddYears(-6), CertificationIssuing.NewMetadata());
        await CertificationDatabase.DeliverAsync(aspire, CertificationIssuing.StartedType, membership, cancellationToken);
        var published = CertificationIssuing.Published();
        await CertificationDatabase.DeliverAsync(aspire, CertificationIssuing.PublishedType, published, cancellationToken);
        var retained = await PassedAsync(membership, published, now.AddYears(-1), cancellationToken);
        var old = await PassedAsync(membership, published, now.AddYears(-5).AddDays(-1), cancellationToken);
        var outside = await PassedAsync(membership, published, now.AddYears(-7), cancellationToken);
        await CertificationDatabase.WaitUntilCountAsync(aspire, StoredOfHolder, probe.Person, 3, cancellationToken);
        return new CertificationPlanted(pharmacy, membership, retained, old, outside);
    }

    private async Task<CertificationIssuing.CertificateRow> PassedAsync(CertificationIssuing.StartedEvent membership, CertificationIssuing.PublishedEvent published, DateTimeOffset passedAt, CancellationToken cancellationToken)
    {
        var passed = CertificationIssuing.Passed(membership, published, passedAt);
        await CertificationDatabase.DeliverAsync(aspire, CertificationIssuing.PassedType, passed, cancellationToken);
        return (await CertificationIssuing.CertificatesAsync(aspire, passed.AttemptId, cancellationToken)).ShouldHaveSingleItem();
    }

    /// <summary>Certification's own container, read as its service reads it.</summary>
    private async Task<AzureStoredBlobs> CertificationBlobsAsync(CancellationToken cancellationToken)
    {
        var connectionString = await aspire.App.GetConnectionStringAsync("certification-blobs", cancellationToken)
            ?? throw new InvalidOperationException("Certification's container has no connection string.");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:blobs"] = connectionString })
            .Build();
        return new AzureStoredBlobs(configuration);
    }

    /// <summary>The certificate ids of the pharmacy's export for the days of the period, from its <c>index.csv</c>.</summary>
    private static async Task<List<Guid>> ExportedIdsAsync(HttpClient manager, Guid pharmacy, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        using var response = await manager.GetAsync(new Uri($"/certification/pharmacies/{pharmacy:D}/certificates/export?from={DayOf(from)}&to={DayOf(to)}", UriKind.Relative), cancellationToken);
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, Encoding.UTF8.GetString(bytes));
        using var zip = new ZipArchive(new MemoryStream(bytes, writable: false), ZipArchiveMode.Read);
        var index = zip.GetEntry("index.csv").ShouldNotBeNull();
        await using var content = await index.OpenAsync(cancellationToken);
        using var reader = new StreamReader(content, Encoding.UTF8);
        var rows = (await reader.ReadToEndAsync(cancellationToken)).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        return [.. rows.Skip(1).Select(row => Guid.Parse(row.Split(';')[0]))];
    }

    /// <summary>The ids of the certificates whose holder name is the needle.</summary>
    private async Task<List<Guid>> CertificateIdsNamedAsync(string name, CancellationToken cancellationToken)
    {
        await using var connection = await CertificationDatabase.OpenAsync(aspire, cancellationToken);
        await using var command = new NpgsqlCommand("select id from certification.certificates where holder_name = @name", connection);
        command.Parameters.AddWithValue("name", name);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        List<Guid> ids = [];
        while (await reader.ReadAsync(cancellationToken))
        {
            ids.Add(reader.GetGuid(0));
        }

        return ids;
    }

    /// <summary>The person to erase: a fresh id, a name and an address only this test uses.</summary>
    private sealed record Probe(Guid Person, string Name, string Address)
    {
        public static Probe New()
        {
            var id = Guid.CreateVersion7();
            return new Probe(id, $"Erasure Probe {id:N}", $"erasure-{id:N}@example.com");
        }
    }

    /// <summary>What <see cref="CertificationPlantedAsync"/> planted: the pharmacy, the probe's membership and their three certificates.</summary>
    private sealed record CertificationPlanted(Guid Pharmacy, CertificationIssuing.StartedEvent Membership, CertificationIssuing.CertificateRow Retained, CertificationIssuing.CertificateRow Old, CertificationIssuing.CertificateRow Outside)
    {
        public IReadOnlyList<CertificationIssuing.CertificateRow> All => [Retained, Old, Outside];
    }

    private sealed record ErasureRead(Guid Id, DateTimeOffset RequestedAt, Guid RequestedBy, bool ConfirmationMailed, DateTimeOffset? ConfirmationHandledAt, IReadOnlyList<ErasureStepRead> Steps);

    private sealed record ErasureStepRead(string Module, DateTimeOffset CompletedAt);

    private sealed record EventMetadata(Guid EventIdentifier, DateTimeOffset OccurredAt);

    private sealed record StartedEvent(Guid MembershipId, Guid PersonId, Guid PharmacyId, string DisplayName, string JobFunction, string Role, DateTimeOffset StartedAt, EventMetadata Metadata);

    private sealed record ChangedEvent(Guid MembershipId, Guid PersonId, Guid PharmacyId, string DisplayName, string JobFunction, string Role, int Version, EventMetadata Metadata);
}
