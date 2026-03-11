using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using YtProducer.Domain.Entities;

namespace YtProducer.Infrastructure.Persistence.Configurations;

public sealed class PromptGenerationOutputConfiguration : IEntityTypeConfiguration<PromptGenerationOutput>
{
    public void Configure(EntityTypeBuilder<PromptGenerationOutput> builder)
    {
        builder.ToTable("prompt_generation_outputs");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(x => x.PromptGenerationId)
            .HasColumnName("prompt_generation_id")
            .IsRequired();

        builder.Property(x => x.OutputType)
            .HasColumnName("output_type")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(x => x.OutputLabel)
            .HasColumnName("output_label")
            .HasMaxLength(255);

        builder.Property(x => x.OutputText)
            .HasColumnName("output_text")
            .HasColumnType("text");

        builder.Property(x => x.OutputJson)
            .HasColumnName("output_json")
            .HasColumnType("jsonb");

        builder.Property(x => x.IsPrimary)
            .HasColumnName("is_primary")
            .IsRequired();

        builder.Property(x => x.IsValid)
            .HasColumnName("is_valid")
            .IsRequired();

        builder.Property(x => x.ValidationErrors)
            .HasColumnName("validation_errors")
            .HasColumnType("text");

        builder.Property(x => x.ProviderResponseJson)
            .HasColumnName("provider_response_json")
            .HasColumnType("jsonb");

        builder.Property(x => x.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired()
            .HasDefaultValueSql("NOW()");

        builder.HasIndex(x => x.PromptGenerationId);
        builder.HasIndex(x => x.CreatedAtUtc);
        builder.HasIndex(x => x.IsPrimary);
    }
}
