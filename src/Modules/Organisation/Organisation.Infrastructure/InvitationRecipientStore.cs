using Microsoft.EntityFrameworkCore;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.InvitationRecipients;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Infrastructure.Persistence;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Infrastructure;

/// <summary>
/// The addresses mailed an invitation, by derived id. The module's unit of work saves what is changed.
/// <para>
/// A recipient is loaded for a mail inside the context's transaction (<see cref="InvitationMailRows"/>): the row is
/// inserted when missing, <see cref="InvitationRecipient.NotYetMailed"/>, with <c>ON CONFLICT (id) DO NOTHING</c>, then
/// locked with <c>FOR UPDATE</c> until the transaction ends, then loaded. A second command mailing the same address
/// waits at the insert (for the row another transaction is inserting) or at the lock, and goes on with the row as the
/// first committed it, so two pharmacies inviting one new address at once no longer collide on the key: the second
/// counts on the first's mail, and within the cooldown is refused as it would be alone (#375). The row version stays as
/// a backstop. The recipient's row is held before the sender's, never after, and a wait for it past
/// <see cref="InvitationMailRows.LockTimeout"/> is a lost race. A command that does not commit rolls its transaction
/// back, with the lock and any new row: at once when it is refused, and otherwise when the request's scope disposes the
/// context, so no row is ever committed without a mail counted on it.
/// </para>
/// </summary>
public sealed class InvitationRecipientStore(OrganisationDbContext context) : IInvitationRecipientStore
{
    public async Task<Result<InvitationRecipient, LostRace>> LoadForMailAsync(EmailAddress address, CancellationToken cancellationToken)
    {
        Ensure.That(address).IsNotNull();

        var missing = InvitationRecipient.NotYetMailed(address);
        var held = await InvitationMailRows.CreateAndHoldAsync(
            context,
            $"""
            INSERT INTO organisation.invitation_recipients (id, last_mailed_at)
            VALUES ({missing.Id.Value}, {missing.LastMailedAt.Value})
            ON CONFLICT (id) DO NOTHING
            """,
            $"SELECT 1 AS \"Value\" FROM organisation.invitation_recipients WHERE id = {missing.Id.Value} FOR UPDATE",
            cancellationToken);
        if (held.IsFailure)
        {
            return Failure(held.Error);
        }

        return Success(await context.Set<InvitationRecipient>().SingleAsync(recipient => recipient.Id == missing.Id, cancellationToken));
    }
}
