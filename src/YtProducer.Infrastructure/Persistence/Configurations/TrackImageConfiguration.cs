using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using YtProducer.Domain.Entities;

namespace YtProducer.Infrastructure.Persistence.Configurations;

public sealed class TrackImageConfiguration : IEntityTypeConfiguration<TrackImage>
{
    public void Configure(EntityTypeBuilder<TrackImage> builder)
    {
        builder.ToTable("track_images");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(x => x.TrackId)
            .HasColumnName("track_id")
            .IsRequired();

        builder.Property(x => x.PlaylistId)
            .HasColumnName("playlist_id")
            .IsRequired();

        builder.Property(x => x.PlaylistPosition)
            .HasColumnName("playlist_position")
            .IsRequired();

        builder.Property(x => x.FileName)
            .HasColumnName("file_name")
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(x => x.FilePath)
            .HasColumnName("file_path")
            .HasMaxLength(2000)
            .IsRequired();

        builder.Property(x => x.SourceUrl)
            .HasColumnName("source_url")
            .HasMaxLength(2000);

        builder.Property(x => x.Model)
            .HasColumnName("model")
            .HasMaxLength(100);

        builder.Property(x => x.Prompt)
            .HasColumnName("prompt")
            .HasColumnType("text");

        builder.Property(x => x.AspectRatio)
            .HasColumnName("aspect_ratio")
            .HasMaxLength(32);

        builder.Property(x => x.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired()
            .HasDefaultValueSql("NOW()");

        builder.HasIndex(x => x.TrackId);
        builder.HasIndex(x => x.PlaylistId);
        builder.HasIndex(x => new { x.PlaylistId, x.PlaylistPosition });
    }
}
