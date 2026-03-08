using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using YtProducer.Domain.Entities;

namespace YtProducer.Infrastructure.Persistence.Configurations;

public sealed class TrackSocialStatConfiguration : IEntityTypeConfiguration<TrackSocialStat>
{
    public void Configure(EntityTypeBuilder<TrackSocialStat> builder)
    {
        builder.ToTable("track_social_stats");

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

        builder.Property(x => x.LikesCount)
            .HasColumnName("likes_count")
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(x => x.DislikesCount)
            .HasColumnName("dislikes_count")
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(x => x.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired()
            .HasDefaultValueSql("NOW()");

        builder.Property(x => x.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .IsRequired()
            .HasDefaultValueSql("NOW()");

        builder.HasIndex(x => x.TrackId)
            .IsUnique();

        builder.HasIndex(x => x.PlaylistId);

        builder.HasOne(x => x.Track)
            .WithOne(x => x.SocialStat)
            .HasForeignKey<TrackSocialStat>(x => x.TrackId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Playlist)
            .WithMany()
            .HasForeignKey(x => x.PlaylistId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
