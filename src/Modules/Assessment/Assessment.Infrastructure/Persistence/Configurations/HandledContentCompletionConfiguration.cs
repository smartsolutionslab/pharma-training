using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.ReadModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Infrastructure.Persistence.Configurations;

/// <summary>One row per <c>ContentCompletedV1</c> handled; its primary key backs the consumer's check that an event is handled once.</summary>
public sealed class HandledContentCompletionConfiguration : IEntityTypeConfiguration<HandledContentCompletion>
{
    public void Configure(EntityTypeBuilder<HandledContentCompletion> builder)
    {
        Ensure.That(builder).IsNotNull();

        builder.ToTable("handled_content_completions");
        builder.HasKey(handled => handled.EventIdentifier).HasName("pk_handled_content_completions");
        builder.Property(handled => handled.EventIdentifier).HasColumnName("event_identifier").ValueGeneratedNever();
        builder.Property(handled => handled.SessionId).HasColumnName("session_id").IsRequired();
        builder.Property(handled => handled.HandledAt).HasColumnName("handled_at").IsRequired();
    }
}
