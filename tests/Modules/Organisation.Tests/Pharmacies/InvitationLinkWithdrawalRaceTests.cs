using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Time.Testing;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Mail;
using SmartSolutionsLab.PharmaTraining.Contracts.Organisation;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.Consumers;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Tests.Pharmacies;

/// <summary>
/// A withdrawal that loses its race to a revoke finds the links cleared already: that is logged at Information, not
/// as a failure, and the send's failure is still thrown (review O2).
/// </summary>
public class InvitationLinkWithdrawalRaceTests
{
    private static readonly PersonId Owner = PersonId.From(Guid.CreateVersion7());
    private static readonly EmailAddress Anna = EmailAddress.From("anna@example.org");

    private readonly InMemoryPharmacyStore store = new();
    private readonly IMailSender mails = Substitute.For<IMailSender>();
    private readonly FakeTimeProvider time = new(TestPharmacies.RegisteredAt.Value.AddMinutes(1));
    private readonly FakeLogger<InvitationMailRequestedV1Consumer> logger = new();

    [Fact]
    public async Task A_withdrawal_that_loses_its_race_to_a_revoke_is_logged_as_information_and_the_send_failure_is_still_thrown()
    {
        var pharmacy = TestPharmacies.Register(Owner, "Apotheke am Markt");
        var membership = TestPharmacies.Invite(pharmacy, Anna.Value);
        pharmacy.Stored(store);
        var refused = new MailNotSentException("The SMTP server refused the mail.", mayHaveBeenDelivered: false);
        mails.SendAsync(Arg.Any<OutgoingMail>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            // A manager's revoke commits while the send fails, so the withdrawal's own commit loses its race.
            RevokedByAnotherWriter(pharmacy, membership);
            store.LoseNextCommit();
            return Task.FromException(refused);
        });

        var thrown = await Should.ThrowAsync<MailNotSentException>(
            () => Consumer().HandleAsync(new InvitationMailRequestedV1(membership.Value, pharmacy.Id.Value), TestContext.Current.CancellationToken));

        thrown.ShouldBeSameAs(refused);
        var records = logger.Collector.GetSnapshot();
        records.ShouldNotContain(record => record.Level >= LogLevel.Warning);
        records.Where(record => record.Level == LogLevel.Information).ShouldHaveSingleItem().Message.ShouldContain(membership.Value.ToString(), Case.Sensitive);
    }

    /// <summary>The pharmacy as a manager's revoke left it in the store: a separate instance, so the consumer's copy stays stale.</summary>
    private void RevokedByAnotherWriter(Pharmacy pharmacy, MembershipId membership)
    {
        var founder = pharmacy.Membership(TestPharmacies.FounderOf(pharmacy));
        var revoked = Pharmacy.Register(pharmacy.Id, pharmacy.Name, founder.Id, Owner, DisplayName.From("Olaf Owner"), founder.Email, JobFunction.Pharmacist, TestPharmacies.RegisteredAt);
        revoked.Invite(membership, Anna, JobFunction.Pta, MembershipRole.Employee, TestPharmacies.RegisteredAt, InvitationMailLimits.Default).IsSuccess.ShouldBeTrue();
        revoked.RevokeInvitation(membership).IsSuccess.ShouldBeTrue();
        revoked.ClearDomainEvents();
        store.Stored[store.Stored.IndexOf(pharmacy)] = revoked;
    }

    private static FrontendLinks Links() => new(new ConfigurationBuilder()
        .AddInMemoryCollection([new KeyValuePair<string, string?>("Frontend:BaseUrl", "https://app.example")])
        .Build());

    private InvitationMailRequestedV1Consumer Consumer() => new(store, store, mails, Links(), time, logger);
}
