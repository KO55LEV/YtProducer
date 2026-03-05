using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using YtProducer.Domain.Entities;

namespace YtProducer.Infrastructure.Persistence.Configurations;

public sealed class TrackConfiguration : IEntityTypeConfiguration<Track>
{
    public void Configure(EntityTypeBuilder<Track> builder)
    {
        builder.ToTable("tracks");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(x => x.PlaylistId)
            .HasColumnName("playlist_id")
            .IsRequired();

        builder.Property(x => x.PlaylistPosition)
            .HasColumnName("playlist_position")
            .IsRequired();

        builder.Property(x => x.Title)
            .HasColumnName("title")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.YouTubeTitle)
            .HasColumnName("youtube_title")
            .HasMaxLength(200);

        builder.Property(x => x.SourceUrl)
            .HasColumnName("source_url")
            .HasMaxLength(1000);

        builder.Property(x => x.Style)
            .HasColumnName("style")
            .HasMaxLength(100);

        builder.Property(x => x.Duration)
            .HasColumnName("duration")
            .HasMaxLength(20);

        builder.Property(x => x.TempoBpm)
            .HasColumnName("tempo_bpm");

        builder.Property(x => x.Key)
            .HasColumnName("key")
            .HasMaxLength(50);

        builder.Property(x => x.EnergyLevel)
            .HasColumnName("energy_level");

        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(x => x.Metadata)
            .HasColumnName("metadata")
            .HasColumnType("jsonb");

        builder.Property(x => x.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired()
            .HasDefaultValueSql("NOW()");

        builder.Property(x => x.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .IsRequired()
            .HasDefaultValueSql("NOW()");

        builder.HasIndex(x => new { x.PlaylistId, x.PlaylistPosition })
            .IsUnique();

        builder.HasIndex(x => x.PlaylistId);
        builder.HasIndex(x => x.Status);
    }
}
