using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.ReadModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Infrastructure.Persistence.Configurations;

/// <summary>The name-free tombstone of an erased person (#115); its primary key backs the check that one is stored once.</summary>
public sealed class ErasedPersonCopyConfiguration : IEntityTypeConfiguration<ErasedPersonCopy>
{
    public void Configure(EntityTypeBuilder<ErasedPersonCopy> builder)
    {
        Ensure.That(builder).IsNotNull();

        builder.ToTable("erased_persons");
        builder.HasKey(erased => erased.PersonId).HasName("pk_erased_persons");
        builder.Property(erased => erased.PersonId).HasColumnName("person_id").ValueGeneratedNever();
        builder.Property(erased => erased.ErasedAt).HasColumnName("erased_at").IsRequired();
    }
}
