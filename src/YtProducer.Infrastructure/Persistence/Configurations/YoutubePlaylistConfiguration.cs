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
            .ValueGeneratedNever();

        builder.Property(x => x.YoutubePlaylistId)
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(x => x.Title)
            .HasMaxLength(255);

        builder.Property(x => x.Description)
            .HasMaxLength(5000);

        builder.Property(x => x.Status)
            .HasMaxLength(32);

        builder.Property(x => x.PrivacyStatus)
            .HasMaxLength(32);

        builder.Property(x => x.ChannelId)
            .HasMaxLength(128);

        builder.Property(x => x.ChannelTitle)
            .HasMaxLength(255);

        builder.Property(x => x.ThumbnailUrl)
            .HasMaxLength(1000);

        builder.Property(x => x.Etag)
            .HasMaxLength(128);

        builder.Property(x => x.Metadata)
            .HasColumnType("jsonb");

        builder.Property(x => x.CreatedAtUtc)
            .IsRequired()
            .HasDefaultValueSql("NOW()");

        builder.Property(x => x.UpdatedAtUtc)
            .IsRequired()
            .HasDefaultValueSql("NOW()");

        builder.Property(x => x.LastSyncedAtUtc);

        builder.Property(x => x.PublishedAtUtc);

        builder.HasIndex(x => x.YoutubePlaylistId)
            .IsUnique();
        builder.HasIndex(x => x.Status);
    }
}
