using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.ReadModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Infrastructure.Persistence.Configurations;

/// <summary>
/// One row per membership whose start Learning received; its primary key backs the consumer's check that a start is
/// stored once. Copied from Compliance's <c>MemberConfiguration.cs</c>, without name, job function and role.
/// </summary>
public sealed class MemberCopyConfiguration : IEntityTypeConfiguration<MemberCopy>
{
    public void Configure(EntityTypeBuilder<MemberCopy> builder)
    {
        Ensure.That(builder).IsNotNull();

        builder.ToTable("members");
        builder.HasKey(member => member.Id).HasName("pk_members");
        builder.Property(member => member.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(member => member.PersonId).HasColumnName("person_id").IsRequired();
        builder.Property(member => member.PharmacyId).HasColumnName("pharmacy_id").IsRequired();

        // Every read is by person: is this learner an active member?
        builder.HasIndex(member => member.PersonId).HasDatabaseName("ix_members_person_id");
    }
}
