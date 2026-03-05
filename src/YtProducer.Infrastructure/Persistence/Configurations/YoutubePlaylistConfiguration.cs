using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using YtProducer.Domain.Entities;

namespace YtProducer.Infrastructure.Persistence.Configurations;

public sealed class YoutubePlaylistConfiguration : IEntityTypeConfiguration<YoutubePlaylist>
{
    public void Configure(EntityTypeBuilder<YoutubePlaylist> builder)
    {
        builder.ToTable("youtube_playlists");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(x => x.PlaylistId)
            .HasColumnName("playlist_id")
            .IsRequired();

        builder.Property(x => x.YoutubePlaylistId)
            .HasColumnName("youtube_playlist_id")
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(x => x.Title)
            .HasColumnName("title")
            .HasMaxLength(255);

        builder.Property(x => x.Description)
            .HasColumnName("description")
            .HasMaxLength(5000);

        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasMaxLength(32);

        builder.Property(x => x.PrivacyStatus)
            .HasColumnName("privacy_status")
            .HasMaxLength(32);

        builder.Property(x => x.ChannelId)
            .HasColumnName("channel_id")
            .HasMaxLength(128);

        builder.Property(x => x.ChannelTitle)
            .HasColumnName("channel_title")
            .HasMaxLength(255);

        builder.Property(x => x.ItemCount)
            .HasColumnName("item_count");

        builder.Property(x => x.ThumbnailUrl)
            .HasColumnName("thumbnail_url")
            .HasMaxLength(1000);

        builder.Property(x => x.Etag)
            .HasColumnName("etag")
            .HasMaxLength(128);

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

        builder.Property(x => x.LastSyncedAtUtc)
            .HasColumnName("last_synced_at_utc");

        builder.Property(x => x.PublishedAtUtc)
            .HasColumnName("published_at_utc");

        builder.HasIndex(x => x.YoutubePlaylistId)
            .IsUnique();
        builder.HasIndex(x => x.PlaylistId);
        builder.HasIndex(x => x.Status);
    }
}
