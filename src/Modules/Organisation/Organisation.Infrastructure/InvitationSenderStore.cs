using Microsoft.EntityFrameworkCore;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.InvitationSenders;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Infrastructure.Persistence;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Infrastructure;

/// <summary>The people who requested invitation mails, with their daily tally. The module's unit of work saves what is added or changed.</summary>
public sealed class InvitationSenderStore(OrganisationDbContext context) : IInvitationSenderStore
{
    public void Add(InvitationSender sender) => context.Add(sender);

    public async Task<Option<InvitationSender>> LoadAsync(PersonId id, CancellationToken cancellationToken) =>
        await context.Set<InvitationSender>().SingleOrDefaultAsync(sender => sender.Id == id, cancellationToken) is { } sender
            ? Option<InvitationSender>.Some(sender)
            : Option<InvitationSender>.None;
}
