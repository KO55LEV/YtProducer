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

        builder.Property(x => x.Theme)
            .HasColumnName("theme")
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(x => x.Model)
            .HasColumnName("model")
            .HasMaxLength(100);

        builder.Property(x => x.InputJson)
            .HasColumnName("input_json")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(x => x.ResolvedPrompt)
            .HasColumnName("resolved_prompt")
            .HasColumnType("text")
            .IsRequired();

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
    }
}
