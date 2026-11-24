using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.InvitationRecipients;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Infrastructure.Persistence.Configurations;

/// <summary>An address mailed an invitation, by its derived id; the address itself is not stored (docs/adr/0004).</summary>
public sealed class InvitationRecipientConfiguration : IEntityTypeConfiguration<InvitationRecipient>
{
    public void Configure(EntityTypeBuilder<InvitationRecipient> builder)
    {
        Ensure.That(builder).IsNotNull();

        builder.ToTable("invitation_recipients");
        builder.HasKey(recipient => recipient.Id);
        builder.Property(recipient => recipient.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(recipient => recipient.LastMailedAt).HasColumnName("last_mailed_at").IsRequired();
        // Two requests that mail the address at once race on this; the loser saves nothing (docs/adr/0002).
        builder.HasRowVersion();
    }
}
