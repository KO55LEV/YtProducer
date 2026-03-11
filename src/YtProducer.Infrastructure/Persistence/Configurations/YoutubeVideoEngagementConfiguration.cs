using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using YtProducer.Domain.Entities;

namespace YtProducer.Infrastructure.Persistence.Configurations;

public sealed class YoutubeVideoEngagementConfiguration : IEntityTypeConfiguration<YoutubeVideoEngagement>
{
    public void Configure(EntityTypeBuilder<YoutubeVideoEngagement> builder)
    {
        builder.ToTable("youtube_video_engagements");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(x => x.ChannelId)
            .HasColumnName("channel_id")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(x => x.YoutubeVideoId)
            .HasColumnName("youtube_video_id")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(x => x.TrackId)
            .HasColumnName("track_id");

        builder.Property(x => x.PlaylistId)
            .HasColumnName("playlist_id");

        builder.Property(x => x.AlbumReleaseId)
            .HasColumnName("album_release_id");

        builder.Property(x => x.EngagementType)
            .HasColumnName("engagement_type")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(x => x.PromptTemplateId)
            .HasColumnName("prompt_template_id");

        builder.Property(x => x.PromptGenerationId)
            .HasColumnName("prompt_generation_id");

        builder.Property(x => x.Provider)
            .HasColumnName("provider")
            .HasMaxLength(32);

        builder.Property(x => x.Model)
            .HasColumnName("model")
            .HasMaxLength(100);

        builder.Property(x => x.GeneratedText)
            .HasColumnName("generated_text")
            .HasColumnType("text");

        builder.Property(x => x.FinalText)
            .HasColumnName("final_text")
            .HasColumnType("text");

        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(x => x.YoutubeCommentId)
            .HasColumnName("youtube_comment_id")
            .HasMaxLength(128);

        builder.Property(x => x.PostedAtUtc)
            .HasColumnName("posted_at_utc");

        builder.Property(x => x.ErrorMessage)
            .HasColumnName("error_message")
            .HasColumnType("text");

        builder.Property(x => x.MetadataJson)
            .HasColumnName("metadata_json")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(x => x.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired()
            .HasDefaultValueSql("NOW()");

        builder.Property(x => x.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .IsRequired()
            .HasDefaultValueSql("NOW()");

        builder.HasIndex(x => x.ChannelId);
        builder.HasIndex(x => x.YoutubeVideoId);
        builder.HasIndex(x => x.TrackId);
        builder.HasIndex(x => x.PlaylistId);
        builder.HasIndex(x => x.AlbumReleaseId);
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.PromptGenerationId);
        builder.HasIndex(x => new { x.ChannelId, x.YoutubeVideoId });
    }
}
