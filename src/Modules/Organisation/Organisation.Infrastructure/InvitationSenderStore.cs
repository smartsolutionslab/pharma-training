using Microsoft.EntityFrameworkCore;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.InvitationSenders;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Infrastructure.Persistence;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Infrastructure;

/// <summary>
/// The people who requested invitation mails, with their daily tally. The module's unit of work saves what is changed.
/// <para>
/// A sender is loaded for a mail inside the context's transaction, begun here when none is open (the unit of work
/// commits it, and the command's domain events wait for that commit): the row is inserted when missing, with
/// <c>ON CONFLICT (person_id) DO NOTHING</c>, then locked with <c>FOR UPDATE</c> until the transaction ends, then
/// loaded. A second command of the same sender waits at the insert (for the row another transaction is inserting) or at
/// the lock, and goes on with the row as the first committed it, so a sender's very first two mails no longer collide
/// on the key and concurrent counts no longer lose on the row version (#369). The row version stays as a backstop. A
/// command that does not commit rolls its new row back with the transaction.
/// </para>
/// </summary>
public sealed class InvitationSenderStore(OrganisationDbContext context) : IInvitationSenderStore
{
    public async Task<InvitationSender> LoadForMailAsync(PersonId id, Timestamp now, CancellationToken cancellationToken)
    {
        Ensure.That(id).IsNotNull();
        Ensure.That(now).IsNotNull();

        if (context.Database.CurrentTransaction is null)
        {
            await context.Database.BeginTransactionAsync(cancellationToken);
        }

        var missing = InvitationSender.NotYetMailed(id, now);
        await context.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO organisation.invitation_senders (person_id, mail_day, mails_that_day)
            VALUES ({missing.Id.Value}, {missing.MailDay.Value}, {missing.MailsThatDay.Value})
            ON CONFLICT (person_id) DO NOTHING
            """,
            cancellationToken);
        await context.Database
            .SqlQuery<int>($"SELECT 1 AS \"Value\" FROM organisation.invitation_senders WHERE person_id = {id.Value} FOR UPDATE")
            .ToListAsync(cancellationToken);

        return await context.Set<InvitationSender>().SingleAsync(sender => sender.Id == id, cancellationToken);
    }
}
