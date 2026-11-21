using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using NSubstitute.ExceptionExtensions;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Mail;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.Reactions;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies.Events;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Tests.Pharmacies;

/// <summary>Each invitation, new or renewed, sends one mail with a link that carries its token; a failed send is not swallowed.</summary>
public class SendInvitationMailTests
{
    private static readonly Timestamp Now = Timestamp.From(new DateTimeOffset(2026, 11, 21, 10, 0, 0, TimeSpan.Zero));

    private readonly IMailSender mails = Substitute.For<IMailSender>();
    private readonly FrontendLinks links = new(new ConfigurationBuilder()
        .AddInMemoryCollection([new KeyValuePair<string, string?>("Frontend:BaseUrl", "https://app.example")])
        .Build());

    [Fact]
    public async Task An_invitation_sends_one_mail_to_the_address_with_a_link_that_carries_its_token()
    {
        List<OutgoingMail> sent = [];
        mails.SendAsync(Arg.Do<OutgoingMail>(sent.Add), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var token = InvitationToken.From(RandomNumberGenerator.GetHexString(43, lowercase: true));

        await new SendInvitationMail(mails, links).ReactAsync(Invited(token), TestContext.Current.CancellationToken);

        var mail = sent.ShouldHaveSingleItem();
        mail.To.ShouldBe("anna@example.org");
        mail.Text.ShouldContain($"https://app.example/invitations/accept#token={token.Value}", Case.Sensitive);
    }

    [Fact]
    public async Task A_mail_that_cannot_be_sent_fails_the_reaction()
    {
        mails.SendAsync(Arg.Any<OutgoingMail>(), Arg.Any<CancellationToken>()).ThrowsAsync(new IOException("The SMTP server is gone."));

        await Should.ThrowAsync<IOException>(
            () => new SendInvitationMail(mails, links).ReactAsync(Invited(InvitationToken.From(new string('t', 43))), TestContext.Current.CancellationToken));
    }

    private static MemberInvited Invited(InvitationToken token)
    {
        var pharmacy = TestPharmacies.Register(PersonId.From(Guid.CreateVersion7()), "Apotheke am Markt");
        pharmacy.ClearDomainEvents();
        pharmacy.Invite(MembershipId.New(), EmailAddress.From("anna@example.org"), token, JobFunction.Pta, MembershipRole.Employee, Now).IsSuccess.ShouldBeTrue();
        return pharmacy.DomainEvents.OfType<MemberInvited>().Single();
    }
}
