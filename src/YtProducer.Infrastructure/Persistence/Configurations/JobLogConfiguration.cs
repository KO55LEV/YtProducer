using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using YtProducer.Domain.Entities;

namespace YtProducer.Infrastructure.Persistence.Configurations;

public sealed class JobLogConfiguration : IEntityTypeConfiguration<JobLog>
{
    public void Configure(EntityTypeBuilder<JobLog> builder)
    {
        builder.ToTable("job_logs");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(x => x.JobId)
            .HasColumnName("job_id")
            .IsRequired();

        builder.Property(x => x.Level)
            .HasColumnName("level")
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(x => x.Message)
            .HasColumnName("message")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(x => x.Metadata)
            .HasColumnName("metadata")
            .HasColumnType("jsonb");

        builder.Property(x => x.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired()
            .HasDefaultValueSql("NOW()");

        builder.HasIndex(x => x.JobId);
        builder.HasIndex(x => x.CreatedAtUtc);
    }
}
