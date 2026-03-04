using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using YtProducer.Domain.Entities;

namespace YtProducer.Infrastructure.Persistence.Configurations;

public sealed class JobConfiguration : IEntityTypeConfiguration<Job>
{
    public void Configure(EntityTypeBuilder<Job> builder)
    {
        builder.ToTable("jobs");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(x => x.Type)
            .HasColumnName("type")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(x => x.TargetType)
            .HasColumnName("target_type")
            .HasMaxLength(32);

        builder.Property(x => x.TargetId)
            .HasColumnName("target_id");

        builder.Property(x => x.JobGroupId)
            .HasColumnName("job_group_id");

        builder.Property(x => x.Sequence)
            .HasColumnName("sequence");

        builder.Property(x => x.Progress)
            .HasColumnName("progress")
            .IsRequired();

        builder.Property(x => x.PayloadJson)
            .HasColumnName("payload_json")
            .HasColumnType("jsonb");

        builder.Property(x => x.ResultJson)
            .HasColumnName("result_json")
            .HasColumnType("jsonb");

        builder.Property(x => x.RetryCount)
            .HasColumnName("retry_count")
            .IsRequired();

        builder.Property(x => x.MaxRetries)
            .HasColumnName("max_retries")
            .IsRequired()
            .HasDefaultValue(3);

        builder.Property(x => x.WorkerId)
            .HasColumnName("worker_id")
            .HasMaxLength(256);

        builder.Property(x => x.LeaseExpiresAt)
            .HasColumnName("lease_expires_at");

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(x => x.StartedAt)
            .HasColumnName("started_at");

        builder.Property(x => x.FinishedAt)
            .HasColumnName("finished_at");

        builder.Property(x => x.LastHeartbeat)
            .HasColumnName("last_heartbeat");

        builder.Property(x => x.ErrorCode)
            .HasColumnName("error_code")
            .HasMaxLength(64);

        builder.Property(x => x.ErrorMessage)
            .HasColumnName("error_message")
            .HasColumnType("text");

        builder.Property(x => x.IdempotencyKey)
            .HasColumnName("idempotency_key")
            .HasMaxLength(128);

        builder.HasIndex(x => new { x.Status, x.CreatedAt });
        builder.HasIndex(x => new { x.TargetType, x.TargetId });
        builder.HasIndex(x => x.LeaseExpiresAt);
        builder.HasIndex(x => x.IdempotencyKey)
            .IsUnique()
            .HasFilter("idempotency_key IS NOT NULL");
    }
}
