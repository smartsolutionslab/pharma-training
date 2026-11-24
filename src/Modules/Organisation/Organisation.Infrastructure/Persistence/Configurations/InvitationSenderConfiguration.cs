using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.InvitationSenders;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Infrastructure.Persistence.Configurations;

/// <summary>A person's tally of invitation mails on their latest counted UTC day, one row per person (docs/adr/0004).</summary>
public sealed class InvitationSenderConfiguration : IEntityTypeConfiguration<InvitationSender>
{
    public void Configure(EntityTypeBuilder<InvitationSender> builder)
    {
        Ensure.That(builder).IsNotNull();

        builder.ToTable("invitation_senders");
        builder.HasKey(sender => sender.Id);
        builder.Property(sender => sender.Id).HasColumnName("person_id").ValueGeneratedNever();
        builder.Property(sender => sender.MailDay).HasColumnName("mail_day").IsRequired();
        builder.Property(sender => sender.MailsThatDay).HasColumnName("mails_that_day").IsRequired();
        // Two requests of one sender at once race on this; the loser saves nothing (docs/adr/0002).
        builder.HasRowVersion();
    }
}
