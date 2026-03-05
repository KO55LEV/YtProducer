using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using YtProducer.Domain.Entities;

namespace YtProducer.Infrastructure.Persistence.Configurations;

public sealed class TrackOnYoutubeConfiguration : IEntityTypeConfiguration<TrackOnYoutube>
{
    public void Configure(EntityTypeBuilder<TrackOnYoutube> builder)
    {
        builder.ToTable("track_on_youtube");

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

        builder.Property(x => x.VideoId)
            .HasColumnName("video_id")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(x => x.Url)
            .HasColumnName("url")
            .HasMaxLength(2000);

        builder.Property(x => x.Title)
            .HasColumnName("title")
            .HasMaxLength(200);

        builder.Property(x => x.Description)
            .HasColumnName("description")
            .HasColumnType("text");

        builder.Property(x => x.Privacy)
            .HasColumnName("privacy")
            .HasMaxLength(32);

        builder.Property(x => x.FilePath)
            .HasColumnName("file_path")
            .HasMaxLength(2000);

        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasMaxLength(32);

        builder.Property(x => x.Metadata)
            .HasColumnName("metadata")
            .HasColumnType("jsonb");

        builder.Property(x => x.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired()
            .HasDefaultValueSql("NOW()");

        builder.HasIndex(x => x.TrackId);
        builder.HasIndex(x => x.PlaylistId);
        builder.HasIndex(x => new { x.PlaylistId, x.PlaylistPosition });
        builder.HasIndex(x => x.VideoId).IsUnique();
    }
}
