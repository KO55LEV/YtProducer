using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using YtProducer.Domain.Entities;

namespace YtProducer.Infrastructure.Persistence.Configurations;

public sealed class AlbumReleaseConfiguration : IEntityTypeConfiguration<AlbumRelease>
{
    public void Configure(EntityTypeBuilder<AlbumRelease> builder)
    {
        builder.ToTable("album_releases");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(x => x.PlaylistId)
            .HasColumnName("playlist_id")
            .IsRequired();

        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(x => x.Title)
            .HasColumnName("title")
            .HasMaxLength(255);

        builder.Property(x => x.Description)
            .HasColumnName("description")
            .HasMaxLength(5000);

        builder.Property(x => x.ThumbnailPath)
            .HasColumnName("thumbnail_path")
            .HasMaxLength(2000);

        builder.Property(x => x.OutputVideoPath)
            .HasColumnName("output_video_path")
            .HasMaxLength(2000);

        builder.Property(x => x.TempRootPath)
            .HasColumnName("temp_root_path")
            .HasMaxLength(2000);

        builder.Property(x => x.YoutubeVideoId)
            .HasColumnName("youtube_video_id")
            .HasMaxLength(128);

        builder.Property(x => x.YoutubeUrl)
            .HasColumnName("youtube_url")
            .HasMaxLength(1000);

        builder.Property(x => x.Metadata)
            .HasColumnName("metadata")
            .HasColumnType("jsonb");

        builder.Property(x => x.FinishedAtUtc)
            .HasColumnName("finished_at_utc");

        builder.Property(x => x.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired()
            .HasDefaultValueSql("NOW()");

        builder.Property(x => x.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .IsRequired()
            .HasDefaultValueSql("NOW()");

        builder.HasOne(x => x.Playlist)
            .WithMany(x => x.AlbumReleases)
            .HasForeignKey(x => x.PlaylistId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.PlaylistId);
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.CreatedAtUtc);
    }
}
