using Microsoft.EntityFrameworkCore;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.InvitationSenders;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Infrastructure.Persistence;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Infrastructure;

/// <summary>
/// The people who requested invitation mails, with their daily tally. The module's unit of work saves what is changed.
/// <para>
/// A sender is loaded for a mail inside the context's transaction (<see cref="InvitationMailRows"/>): the row is
/// inserted when missing, with <c>ON CONFLICT (person_id) DO NOTHING</c>, then locked with <c>FOR UPDATE</c> until the
/// transaction ends, then loaded. A second command of the same sender waits at the insert (for the row another
/// transaction is inserting) or at the lock, and goes on with the row as the first committed it, so a sender's very
/// first two mails no longer collide on the key and concurrent counts no longer lose on the row version (#369). The row
/// version stays as a backstop. The sender's row is held after the recipient's, never before (#375), and a wait for it
/// past <see cref="InvitationMailRows.LockTimeout"/> is a lost race. A command that does not commit rolls its
/// transaction back, with the lock and any new row: at once when it is refused, and otherwise when the request's scope
/// disposes the context.
/// </para>
/// </summary>
public sealed class InvitationSenderStore(OrganisationDbContext context) : IInvitationSenderStore
{
    public async Task<Result<InvitationSender, LostRace>> LoadForMailAsync(PersonId id, Timestamp now, CancellationToken cancellationToken)
    {
        Ensure.That(id).IsNotNull();
        Ensure.That(now).IsNotNull();

        var missing = InvitationSender.NotYetMailed(id, now);
        var held = await InvitationMailRows.CreateAndHoldAsync(
            context,
            $"""
            INSERT INTO organisation.invitation_senders (person_id, mail_day, mails_that_day)
            VALUES ({missing.Id.Value}, {missing.MailDay.Value}, {missing.MailsThatDay.Value})
            ON CONFLICT (person_id) DO NOTHING
            """,
            $"SELECT 1 AS \"Value\" FROM organisation.invitation_senders WHERE person_id = {id.Value} FOR UPDATE",
            cancellationToken);
        if (held.IsFailure)
        {
            return Failure(held.Error);
        }

        return Success(await context.Set<InvitationSender>().SingleAsync(sender => sender.Id == id, cancellationToken));
    }

    public async Task RemoveAsync(PersonId id, CancellationToken cancellationToken)
    {
        Ensure.That(id).IsNotNull();

        if (await context.Set<InvitationSender>().SingleOrDefaultAsync(sender => sender.Id == id, cancellationToken) is { } sender)
        {
            context.Remove(sender);
        }
    }
}
