using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.ReadModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Infrastructure.Persistence.Configurations;

/// <summary>
/// One row per stored event Learning announced (#277); its primary key lets one of the report, the sweep and every
/// replica's sweep commit the announcement of an event, and rolls the others back with their envelope.
/// </summary>
public sealed class AnnouncedEventConfiguration : IEntityTypeConfiguration<AnnouncedEvent>
{
    public void Configure(EntityTypeBuilder<AnnouncedEvent> builder)
    {
        Ensure.That(builder).IsNotNull();

        builder.ToTable("announced_events");
        builder.HasKey(announced => new { announced.StreamId, announced.StreamVersion }).HasName("pk_announced_events");
        builder.Property(announced => announced.StreamId).HasColumnName("stream_id").ValueGeneratedNever();
        builder.Property(announced => announced.StreamVersion).HasColumnName("stream_version").ValueGeneratedNever();
        builder.Property(announced => announced.AnnouncedAt).HasColumnName("announced_at").IsRequired();
    }
}
