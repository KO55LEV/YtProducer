using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using YtProducer.Domain.Entities;

namespace YtProducer.Infrastructure.Persistence.Configurations;

public sealed class PromptGenerationConfiguration : IEntityTypeConfiguration<PromptGeneration>
{
    public void Configure(EntityTypeBuilder<PromptGeneration> builder)
    {
        builder.ToTable("prompt_generations");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(x => x.TemplateId)
            .HasColumnName("template_id")
            .IsRequired();

        builder.Property(x => x.Purpose)
            .HasColumnName("purpose")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(x => x.Provider)
            .HasColumnName("provider")
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(x => x.Model)
            .HasColumnName("model")
            .HasMaxLength(100);

        builder.Property(x => x.InputLabel)
            .HasColumnName("input_label")
            .HasMaxLength(255);

        builder.Property(x => x.InputJson)
            .HasColumnName("input_json")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(x => x.ResolvedSystemPrompt)
            .HasColumnName("resolved_system_prompt")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(x => x.ResolvedUserPrompt)
            .HasColumnName("resolved_user_prompt")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(x => x.LatencyMs)
            .HasColumnName("latency_ms");

        builder.Property(x => x.TokenUsageJson)
            .HasColumnName("token_usage_json")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(x => x.RunMetadataJson)
            .HasColumnName("run_metadata_json")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(x => x.TargetType)
            .HasColumnName("target_type")
            .HasMaxLength(64);

        builder.Property(x => x.TargetId)
            .HasColumnName("target_id")
            .HasMaxLength(255);

        builder.Property(x => x.JobId)
            .HasColumnName("job_id");

        builder.Property(x => x.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired()
            .HasDefaultValueSql("NOW()");

        builder.Property(x => x.StartedAtUtc)
            .HasColumnName("started_at_utc");

        builder.Property(x => x.FinishedAtUtc)
            .HasColumnName("finished_at_utc");

        builder.Property(x => x.ErrorMessage)
            .HasColumnName("error_message")
            .HasColumnType("text");

        builder.HasMany(x => x.Outputs)
            .WithOne(x => x.PromptGeneration)
            .HasForeignKey(x => x.PromptGenerationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.TemplateId);
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.CreatedAtUtc);
        builder.HasIndex(x => x.Purpose);
        builder.HasIndex(x => x.Provider);
        builder.HasIndex(x => new { x.TargetType, x.TargetId });
    }
}
