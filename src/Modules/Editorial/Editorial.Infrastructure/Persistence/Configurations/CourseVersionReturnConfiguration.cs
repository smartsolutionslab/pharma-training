using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Infrastructure.Persistence.Configurations;

/// <summary>
/// Every return of a course version, in a table of its own beside the version, which has no navigation to it and does
/// not load it (ADR 0013). A record is only ever inserted, in the save that returns the version.
/// </summary>
public sealed class CourseVersionReturnConfiguration : IEntityTypeConfiguration<CourseVersionReturn>
{
    public void Configure(EntityTypeBuilder<CourseVersionReturn> builder)
    {
        Ensure.That(builder).IsNotNull();

        builder.ToTable("course_version_returns");
        builder.HasKey(record => record.Id);
        // The identifier and the value objects map to their backing values by convention (module.ddd-depth: strict).
        builder.Property(record => record.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(record => record.Version).HasColumnName("course_version_id").IsRequired();
        builder.Property(record => record.ReturnedBy).HasColumnName("returned_by").IsRequired();
        builder.Property(record => record.ReturnedAt).HasColumnName("returned_at").IsRequired();
        builder.Property(record => record.Reason).HasColumnName("reason").HasMaxLength(ReturnReason.MaxLength).IsRequired();

        builder.HasOne<CourseVersion>()
            .WithMany()
            .HasForeignKey(record => record.Version)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(record => new { record.Version, record.ReturnedAt });
    }
}
