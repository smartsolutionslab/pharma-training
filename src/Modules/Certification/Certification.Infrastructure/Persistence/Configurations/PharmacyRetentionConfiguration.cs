using System.Globalization;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Retention;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Infrastructure.Persistence.Configurations;

public sealed class PharmacyRetentionConfiguration : IEntityTypeConfiguration<PharmacyRetention>
{
    public void Configure(EntityTypeBuilder<PharmacyRetention> builder)
    {
        Ensure.That(builder).IsNotNull();

        // No foreign key: a pharmacy has no table in Certification, only memberships copied from Organisation. No row is
        // the default of five years. The check constraint backs RetentionPeriod's range (#391, docs/adr/0039).
        var range = string.Create(CultureInfo.InvariantCulture, $"years BETWEEN {RetentionPeriod.MinimumYears} AND {RetentionPeriod.MaximumYears}");
        builder.ToTable("pharmacy_retention_periods", table => table.HasCheckConstraint("ck_pharmacy_retention_periods_years", range));
        builder.HasKey(retention => retention.Id).HasName("pk_pharmacy_retention_periods");
        // The identifier and the value object map to their backing values by convention (module.ddd-depth: strict).
        builder.Property(retention => retention.Id).HasColumnName("pharmacy_id").ValueGeneratedNever();
        builder.Property(retention => retention.Period).HasColumnName("years").IsRequired();
    }
}
