using System.Security.Cryptography;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Time.Testing;
using NSubstitute.ExceptionExtensions;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Mail;
using SmartSolutionsLab.PharmaTraining.Contracts.Organisation;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.Consumers;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Erasures;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.InvitationRecipients;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.InvitationSenders;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Tests.Pharmacies;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Tests.Erasures;

/// <summary>
/// The confirmation mail of an erasure (#115) goes to each of the person's addresses once, before Organisation forgets
/// them; then their names and addresses, invitation recipients and sender are gone, and the erasure is announced with
/// Organisation's step done, in one commit. A redelivery mails nothing again, and a failed send forgets nothing.
/// </summary>
public class ErasureConfirmationMailRequestedV1ConsumerTests
{
    private static readonly PersonId Owner = PersonId.From(Guid.CreateVersion7());
    private static readonly PersonId Support = PersonId.From(Guid.CreateVersion7());
    private static readonly DateTimeOffset Start = new(2027, 5, 3, 10, 0, 0, TimeSpan.Zero);

    private readonly InMemoryPharmacyStore store = new();
    private readonly IMailSender mails = Substitute.For<IMailSender>();
    private readonly FakeTimeProvider time = new(Start);
    private readonly FakeLogger<ErasureConfirmationMailRequestedV1Consumer> logger = new();
    private readonly List<OutgoingMail> sent = [];
    private readonly List<IReadOnlyList<EmailAddress?>> addressesAtSend = [];
    private readonly PersonId person = PersonId.From(Guid.CreateVersion7());

    public ErasureConfirmationMailRequestedV1ConsumerTests()
    {
        mails.SendAsync(Arg.Do<OutgoingMail>(mail =>
        {
            sent.Add(mail);
            addressesAtSend.Add([.. store.Stored.SelectMany(pharmacy => pharmacy.Memberships).Where(m => m.Person == person).Select(m => (EmailAddress?)m.Email)]);
        }), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
    }

    [Fact]
    public async Task Each_address_is_mailed_once_then_name_and_address_are_forgotten()
    {
        var setup = ReleasedPersonWithTwoAddresses();

        await Consumer().HandleAsync(new ErasureConfirmationMailRequestedV1(setup.Erasure.Id.Value, person.Value), TestContext.Current.CancellationToken);

        sent.Select(mail => mail.To).ShouldBe([setup.First.Value, setup.Second.Value], ignoreOrder: true);
        sent.ShouldAllBe(mail => mail.Subject == "Ihre Daten bei PharmaTraining wurden gelöscht");
        addressesAtSend.ShouldAllBe(addresses => addresses.All(address => address != null), "an address was forgotten before its mail was sent");
        foreach (var membership in store.Stored.SelectMany(pharmacy => pharmacy.Memberships).Where(m => m.Person == person))
        {
            membership.Name.ShouldBeNull();
            membership.Email.ShouldBeNull();
        }

        var revoked = setup.Elsewhere.Membership(setup.RevokedInvitation);
        revoked.Status.ShouldBe(MembershipStatus.Revoked);
        revoked.Email.ShouldBeNull();
        setup.Home.Membership(TestPharmacies.FounderOf(setup.Home)).Name.ShouldBe(DisplayName.From("Olaf Owner"));
        store.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task The_erasure_is_announced_with_the_organisation_step_done()
    {
        var setup = ReleasedPersonWithTwoAddresses();
        time.Advance(TimeSpan.FromMinutes(2));

        await Consumer().HandleAsync(new ErasureConfirmationMailRequestedV1(setup.Erasure.Id.Value, person.Value), TestContext.Current.CancellationToken);

        store.Published.ShouldHaveSingleItem().ShouldBe(new PersonErasureRequestedV1(setup.Erasure.Id.Value, person.Value));
        setup.Erasure.Confirmation.ShouldBe(ErasureConfirmation.Mailed);
        setup.Erasure.ConfirmationHandledAt.ShouldBe(Timestamp.From(Start.AddMinutes(2)));
        var step = setup.Erasure.Steps.ShouldHaveSingleItem();
        step.Module.ShouldBe(ErasureModule.Organisation);
        step.CompletedAt.ShouldBe(Timestamp.From(Start.AddMinutes(2)));
    }

    [Fact]
    public async Task A_redelivery_sends_nothing_and_changes_nothing()
    {
        var setup = ReleasedPersonWithTwoAddresses();
        var message = new ErasureConfirmationMailRequestedV1(setup.Erasure.Id.Value, person.Value);
        await Consumer().HandleAsync(message, TestContext.Current.CancellationToken);
        time.Advance(TimeSpan.FromHours(1));

        await Consumer().HandleAsync(message, TestContext.Current.CancellationToken);

        sent.Count.ShouldBe(2);
        store.Published.OfType<PersonErasureRequestedV1>().ShouldHaveSingleItem();
        store.Commits.ShouldBe(1);
        setup.Erasure.ConfirmationHandledAt.ShouldBe(Timestamp.From(Start));
        setup.Erasure.Steps.ShouldHaveSingleItem().CompletedAt.ShouldBe(Timestamp.From(Start));
    }

    [Fact]
    public async Task A_person_without_an_address_gets_no_mail_and_is_still_announced()
    {
        var erasure = StoredErasure();

        await Consumer().HandleAsync(new ErasureConfirmationMailRequestedV1(erasure.Id.Value, person.Value), TestContext.Current.CancellationToken);

        sent.ShouldBeEmpty();
        await mails.DidNotReceiveWithAnyArgs().SendAsync(default!, TestContext.Current.CancellationToken);
        erasure.Confirmation.ShouldBe(ErasureConfirmation.NoAddress);
        erasure.ConfirmationHandledAt.ShouldBe(Timestamp.From(Start));
        erasure.Steps.ShouldHaveSingleItem().Module.ShouldBe(ErasureModule.Organisation);
        store.Published.ShouldHaveSingleItem().ShouldBe(new PersonErasureRequestedV1(erasure.Id.Value, person.Value));
        store.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task A_failed_send_forgets_nothing_and_rethrows()
    {
        var setup = ReleasedPersonWithTwoAddresses();
        mails.SendAsync(Arg.Any<OutgoingMail>(), Arg.Any<CancellationToken>()).ThrowsAsync(new IOException("The SMTP server is gone."));

        await Should.ThrowAsync<IOException>(
            () => Consumer().HandleAsync(new ErasureConfirmationMailRequestedV1(setup.Erasure.Id.Value, person.Value), TestContext.Current.CancellationToken));

        setup.Home.Membership(setup.HomeMembership).Name.ShouldBe(DisplayName.From("Erika Erased"));
        setup.Home.Membership(setup.HomeMembership).Email.ShouldBe(setup.First);
        setup.Elsewhere.Membership(setup.RevokedInvitation).Email.ShouldBe(setup.First);
        store.StoredRecipients.Select(recipient => recipient.Id).ShouldContain(InvitationRecipientId.For(setup.First));
        store.StoredSenders.Select(sender => sender.Id).ShouldContain(person);
        setup.Erasure.Confirmation.ShouldBe(ErasureConfirmation.Pending);
        setup.Erasure.Steps.ShouldBeEmpty();
        store.Published.ShouldBeEmpty();
        store.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task Recipients_and_the_sender_are_removed()
    {
        var setup = ReleasedPersonWithTwoAddresses();
        var unrelatedRecipient = InvitationRecipient.FirstMailed(EmailAddress.From("unrelated@example.org"), Timestamp.From(Start));
        var unrelatedSender = InvitationSender.FirstMailed(Owner, Timestamp.From(Start));
        store.StoredRecipients.Add(unrelatedRecipient);
        store.StoredSenders.Add(unrelatedSender);

        await Consumer().HandleAsync(new ErasureConfirmationMailRequestedV1(setup.Erasure.Id.Value, person.Value), TestContext.Current.CancellationToken);

        store.StoredRecipients.ShouldBe([unrelatedRecipient]);
        store.StoredSenders.ShouldBe([unrelatedSender]);
    }

    [Fact]
    public async Task An_unknown_erasure_is_dropped()
    {
        var setup = ReleasedPersonWithTwoAddresses();

        await Consumer().HandleAsync(new ErasureConfirmationMailRequestedV1(Guid.CreateVersion7(), person.Value), TestContext.Current.CancellationToken);

        sent.ShouldBeEmpty();
        setup.Home.Membership(setup.HomeMembership).Name.ShouldBe(DisplayName.From("Erika Erased"));
        store.Published.ShouldBeEmpty();
        store.Commits.ShouldBe(0);
        logger.Collector.Count.ShouldBeGreaterThan(0, "a dropped message is logged");
    }

    private ErasureConfirmationMailRequestedV1Consumer Consumer() => new(store, store, store, store, store, mails, time, logger);

    private Erasure StoredErasure()
    {
        var erasure = Erasure.Request(ErasureId.New(), Support, Timestamp.From(Start));
        store.StoredErasures.Add(erasure);
        return erasure;
    }

    /// <summary>
    /// The state the request leaves: the person's two memberships, under two addresses, ended; an invitation to the first
    /// address revoked in a second pharmacy; a recipient row per address and the person's sender row; the erasure logged.
    /// </summary>
    private Released ReleasedPersonWithTwoAddresses()
    {
        var home = TestPharmacies.Register(Owner, "Stadt-Apotheke");
        var homeMembership = TestPharmacies.AddMember(home, person, MembershipRole.Employee, name: "Erika Erased");
        var first = home.Membership(homeMembership).Email!;
        home.EndMembership(homeMembership, TestPharmacies.RegisteredAt.PlusDays(1)).IsSuccess.ShouldBeTrue();
        home.Stored(store);

        var elsewhere = TestPharmacies.Register(Owner, "Markt-Apotheke");
        var second = EmailAddress.From($"erika-{person.Value:N}@example.net");
        var secondMembership = AcceptAs(elsewhere, person, second, "Erika Erased");
        elsewhere.EndMembership(secondMembership, TestPharmacies.RegisteredAt.PlusDays(1)).IsSuccess.ShouldBeTrue();
        var revokedInvitation = TestPharmacies.Invite(elsewhere, first.Value);
        elsewhere.RevokeInvitation(revokedInvitation).IsSuccess.ShouldBeTrue();
        elsewhere.Stored(store);

        store.StoredRecipients.Add(InvitationRecipient.FirstMailed(first, TestPharmacies.RegisteredAt));
        store.StoredRecipients.Add(InvitationRecipient.FirstMailed(second, TestPharmacies.RegisteredAt));
        store.StoredSenders.Add(InvitationSender.FirstMailed(person, TestPharmacies.RegisteredAt));

        return new Released(StoredErasure(), home, homeMembership, elsewhere, revokedInvitation, first, second);
    }

    private static MembershipId AcceptAs(Pharmacy pharmacy, PersonId person, EmailAddress address, string name)
    {
        var token = InvitationToken.From(RandomNumberGenerator.GetHexString(64));
        var membership = MembershipId.New();
        pharmacy.Invite(membership, address, JobFunction.Pta, MembershipRole.Employee, TestPharmacies.RegisteredAt, InvitationMailLimits.Default).IsSuccess.ShouldBeTrue();
        pharmacy.IssueInvitationLink(membership, token, TestPharmacies.RegisteredAt).IsSuccess.ShouldBeTrue();
        pharmacy.AcceptInvitation(token, person, DisplayName.From(name), address, TestPharmacies.RegisteredAt).IsSuccess.ShouldBeTrue();
        return membership;
    }

    private sealed record Released(
        Erasure Erasure,
        Pharmacy Home,
        MembershipId HomeMembership,
        Pharmacy Elsewhere,
        MembershipId RevokedInvitation,
        EmailAddress First,
        EmailAddress Second);
}
