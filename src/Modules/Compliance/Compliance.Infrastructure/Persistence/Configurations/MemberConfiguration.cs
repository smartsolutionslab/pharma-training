using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Members;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Infrastructure.Persistence.Configurations;

public sealed class MemberConfiguration : IEntityTypeConfiguration<Member>
{
    private const int EnumLength = 16;

    public void Configure(EntityTypeBuilder<Member> builder)
    {
        Ensure.That(builder).IsNotNull();

        builder.ToTable("members");
        builder.HasKey(member => member.Id).HasName("pk_members");
        // The identifiers and the value objects map to their backing values by convention (module.ddd-depth: strict).
        builder.Property(member => member.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(member => member.Person).HasColumnName("person_id").IsRequired();
        builder.Property(member => member.Pharmacy).HasColumnName("pharmacy_id").IsRequired();
        // Whatever Organisation accepted, without a length of Compliance's own.
        builder.Property(member => member.Name).HasColumnName("display_name").HasColumnType("text").IsRequired();
        builder.Property(member => member.JobFunction).HasColumnName("job_function").HasConversion<string>().HasMaxLength(EnumLength).IsRequired();
        builder.Property(member => member.Role).HasColumnName("role").HasConversion<string>().HasMaxLength(EnumLength).IsRequired();

        builder.HasIndex(member => member.Pharmacy).HasDatabaseName("ix_members_pharmacy_id");
        builder.HasIndex(member => member.Person).HasDatabaseName("ix_members_person_id");
    }
}
