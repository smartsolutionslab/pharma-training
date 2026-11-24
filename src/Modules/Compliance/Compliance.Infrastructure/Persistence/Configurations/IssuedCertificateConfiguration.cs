using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Progress;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Infrastructure.Persistence.Configurations;

public sealed class IssuedCertificateConfiguration : IEntityTypeConfiguration<IssuedCertificate>
{
    public void Configure(EntityTypeBuilder<IssuedCertificate> builder)
    {
        Ensure.That(builder).IsNotNull();

        // No foreign keys: the copies arrive in any order, before or after the course versions and assignments.
        builder.ToTable("issued_certificates");
        builder.HasKey(certificate => certificate.Id).HasName("pk_issued_certificates");
        // The identifiers and the value objects map to their backing values by convention (module.ddd-depth: strict).
        builder.Property(certificate => certificate.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(certificate => certificate.Learner).HasColumnName("learner_id").IsRequired();
        builder.Property(certificate => certificate.Course).HasColumnName("course_id").IsRequired();
        builder.Property(certificate => certificate.Version).HasColumnName("course_version_id").IsRequired();
        builder.Property(certificate => certificate.IssuedAt).HasColumnName("issued_at").IsRequired();

        // Backs reading a learner's certificates of the courses on the dashboard.
        builder.HasIndex(certificate => new { certificate.Learner, certificate.Course }).HasDatabaseName("ix_issued_certificates_learner_id_course_id");
    }
}
