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
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(x => x.RawText)
            .HasColumnName("raw_text")
            .HasColumnType("text");

        builder.Property(x => x.FormattedJson)
            .HasColumnName("formatted_json")
            .HasColumnType("jsonb");

        builder.Property(x => x.IsValidJson)
            .HasColumnName("is_valid_json")
            .IsRequired();

        builder.Property(x => x.ValidationErrors)
            .HasColumnName("validation_errors")
            .HasColumnType("text");

        builder.Property(x => x.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired()
            .HasDefaultValueSql("NOW()");

        builder.HasIndex(x => x.PromptGenerationId);
        builder.HasIndex(x => x.CreatedAtUtc);
    }
}
