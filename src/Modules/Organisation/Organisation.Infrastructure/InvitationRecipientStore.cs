using Microsoft.EntityFrameworkCore;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.InvitationRecipients;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Infrastructure.Persistence;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Infrastructure;

/// <summary>The addresses mailed an invitation, by derived id. The module's unit of work saves what is added or changed.</summary>
public sealed class InvitationRecipientStore(OrganisationDbContext context) : IInvitationRecipientStore
{
    public void Add(InvitationRecipient recipient) => context.Add(recipient);

    public async Task<Option<InvitationRecipient>> LoadAsync(InvitationRecipientId id, CancellationToken cancellationToken) =>
        await context.Set<InvitationRecipient>().SingleOrDefaultAsync(recipient => recipient.Id == id, cancellationToken) is { } recipient
            ? Option<InvitationRecipient>.Some(recipient)
            : Option<InvitationRecipient>.None;

    public async Task RemoveAsync(InvitationRecipientId id, CancellationToken cancellationToken)
    {
        Ensure.That(id).IsNotNull();

        if (await context.Set<InvitationRecipient>().SingleOrDefaultAsync(recipient => recipient.Id == id, cancellationToken) is { } recipient)
        {
            context.Remove(recipient);
        }
    }
}
