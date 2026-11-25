using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Infrastructure.Persistence.Configurations;

/// <summary>The name-free tombstone of an erased person (#115).</summary>
public sealed class ErasedPersonConfiguration : IEntityTypeConfiguration<ErasedPerson>
{
    public void Configure(EntityTypeBuilder<ErasedPerson> builder)
    {
        Ensure.That(builder).IsNotNull();

        builder.ToTable("erased_persons");
        builder.HasKey(erased => erased.Person).HasName("pk_erased_persons");
        // The identifier and the timestamp map to their backing values by convention (module.ddd-depth: strict).
        builder.Property(erased => erased.Person).HasColumnName("person_id").ValueGeneratedNever();
        builder.Property(erased => erased.ErasedAt).HasColumnName("erased_at").IsRequired();
    }
}
