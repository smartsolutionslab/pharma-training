using System.Buffers.Text;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Persistence;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Infrastructure.Persistence;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Tests.Pharmacies;

/// <summary>
/// An invitation's links and its mail record are checked against the membership's own row version, not the pharmacy's
/// (docs/adr/0037): the mail consumer saving them leaves the pharmacy's row alone, so a second invitation that loaded the
/// pharmacy before it does not lose a race nobody ran. Every other change under the pharmacy still updates its row.
/// </summary>
public sealed class InvitationMailRowVersionTests : IDisposable
{
    private static readonly PersonId Owner = PersonId.From(Guid.CreateVersion7());

    private readonly OrganisationDbContext context = new OrganisationDbContextFactory().CreateDbContext([]);
    private readonly Pharmacy pharmacy = TestPharmacies.Register(Owner);

    [Fact]
    public void A_membership_carries_a_row_version_on_xmin()
    {
        var rowVersion = context.Model.FindEntityType(typeof(Membership))!.FindProperty(RowVersion.PropertyName);

        rowVersion.ShouldNotBeNull();
        rowVersion.IsConcurrencyToken.ShouldBeTrue();
        rowVersion.GetColumnName().ShouldBe("xmin");
    }

    [Fact]
    public async Task Issuing_an_invitation_link_updates_the_membership_and_leaves_the_pharmacy_alone()
    {
        var invited = TestPharmacies.Invite(pharmacy, "bert@example.org");
        context.Attach(pharmacy);

        pharmacy.IssueInvitationLink(invited, NewToken(), TestPharmacies.RegisteredAt).IsSuccess.ShouldBeTrue();
        await BumpAsync();

        context.Entry(pharmacy).State.ShouldBe(EntityState.Unchanged);
        context.Entry(MembershipOf(invited)).State.ShouldBe(EntityState.Modified);
    }

    [Fact]
    public async Task Recording_an_invitation_mail_updates_the_membership_and_leaves_the_pharmacy_alone()
    {
        var invited = TestPharmacies.Invite(pharmacy, "bert@example.org");
        pharmacy.IssueInvitationLink(invited, NewToken(), TestPharmacies.RegisteredAt).IsSuccess.ShouldBeTrue();
        context.Attach(pharmacy);

        pharmacy.RecordInvitationMailed(invited, MembershipOf(invited).InvitationVersion).IsSuccess.ShouldBeTrue();
        await BumpAsync();

        context.Entry(pharmacy).State.ShouldBe(EntityState.Unchanged);
        context.Entry(MembershipOf(invited)).State.ShouldBe(EntityState.Modified);
    }

    [Fact]
    public async Task Withdrawing_an_invitation_link_updates_the_membership_and_leaves_the_pharmacy_alone()
    {
        var invited = TestPharmacies.Invite(pharmacy, "bert@example.org");
        var token = NewToken();
        pharmacy.IssueInvitationLink(invited, token, TestPharmacies.RegisteredAt).IsSuccess.ShouldBeTrue();
        context.Attach(pharmacy);

        pharmacy.WithdrawInvitationLink(invited, token).IsSuccess.ShouldBeTrue();
        await BumpAsync();

        context.Entry(pharmacy).State.ShouldBe(EntityState.Unchanged);
        context.Entry(MembershipOf(invited)).State.ShouldBe(EntityState.Modified);
    }

    [Fact]
    public async Task A_new_invitation_still_updates_the_pharmacy()
    {
        context.Attach(pharmacy);

        TestPharmacies.Invite(pharmacy, "bert@example.org");
        await BumpAsync();

        context.Entry(pharmacy).State.ShouldBe(EntityState.Modified);
    }

    [Fact]
    public async Task A_revoked_invitation_still_updates_the_pharmacy()
    {
        var invited = TestPharmacies.Invite(pharmacy, "bert@example.org");
        pharmacy.IssueInvitationLink(invited, NewToken(), TestPharmacies.RegisteredAt).IsSuccess.ShouldBeTrue();
        context.Attach(pharmacy);

        pharmacy.RevokeInvitation(invited).IsSuccess.ShouldBeTrue();
        await BumpAsync();

        context.Entry(pharmacy).State.ShouldBe(EntityState.Modified);
    }

    public void Dispose() => context.Dispose();

    private static InvitationToken NewToken() => InvitationToken.From(Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32)));

    private Membership MembershipOf(MembershipId id) => pharmacy.Memberships.Single(membership => membership.Id == id);

    private async Task BumpAsync() =>
        await new RowVersionBump().SavingChangesAsync(new DbContextEventData(null!, null!, context), default, TestContext.Current.CancellationToken);
}
