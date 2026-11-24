using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Certificates;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Infrastructure.Persistence.Configurations;

public sealed class CertificateConfiguration : IEntityTypeConfiguration<Certificate>
{
    private const int EnumLength = 16;

    public void Configure(EntityTypeBuilder<Certificate> builder)
    {
        Ensure.That(builder).IsNotNull();

        builder.ToTable("certificates");
        builder.HasKey(certificate => certificate.Id).HasName("pk_certificates");
        // The identifiers and the value objects map to their backing values by convention (module.ddd-depth: strict).
        builder.Property(certificate => certificate.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(certificate => certificate.Attempt).HasColumnName("attempt_id").IsRequired();
        builder.Property(certificate => certificate.Holder).HasColumnName("holder_id").IsRequired();
        builder.Property(certificate => certificate.HolderName).HasColumnName("holder_name").HasMaxLength(HolderName.MaxLength).IsRequired();
        builder.Property(certificate => certificate.Course).HasColumnName("course_id").IsRequired();
        builder.Property(certificate => certificate.CourseVersion).HasColumnName("course_version_id").IsRequired();
        builder.Property(certificate => certificate.CourseTitle).HasColumnName("course_title").HasMaxLength(CourseTitle.MaxLength).IsRequired();
        builder.Property(certificate => certificate.CourseVersionNumber).HasColumnName("course_version_number").IsRequired();
        builder.Property(certificate => certificate.Score).HasColumnName("score_percent").IsRequired();
        builder.Property(certificate => certificate.IssuedAt).HasColumnName("issued_at").IsRequired();
        builder.Property(certificate => certificate.ValidUntil).HasColumnName("valid_until").IsRequired();
        builder.Property(certificate => certificate.VerificationCode).HasColumnName("verification_code").HasMaxLength(VerificationCode.MaxLength).IsRequired();
        builder.Property(certificate => certificate.Document).HasColumnName("document_state").HasConversion<string>().HasMaxLength(EnumLength).IsRequired();
        builder.Property(certificate => certificate.DocumentLocation).HasColumnName("document_location").HasMaxLength(DocumentReference.MaxLength);

        // One certificate per passed attempt, backed twice: the consumer returns for an attempt it finds, and this index
        // refuses a concurrent delivery's second certificate, whose retry then finds the first.
        builder.HasIndex(certificate => certificate.Attempt).IsUnique().HasDatabaseName("ix_certificates_attempt_id");
        // A verification code names one certificate; a collision is refused, and the retry draws another code.
        builder.HasIndex(certificate => certificate.VerificationCode).IsUnique().HasDatabaseName("ix_certificates_verification_code");
    }
}
