using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using YtProducer.Domain.Entities;

namespace YtProducer.Infrastructure.Persistence.Configurations;

public sealed class YoutubeUploadQueueConfiguration : IEntityTypeConfiguration<YoutubeUploadQueue>
{
    public void Configure(EntityTypeBuilder<YoutubeUploadQueue> builder)
    {
        builder.ToTable("youtube_upload_queue");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(x => x.Priority)
            .HasColumnName("priority")
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(x => x.Title)
            .HasColumnName("title")
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(x => x.Description)
            .HasColumnName("description")
            .HasMaxLength(5000);

        builder.Property(x => x.Tags)
            .HasColumnName("tags")
            .HasColumnType("text[]");

        builder.Property(x => x.CategoryId)
            .HasColumnName("category_id")
            .HasDefaultValue(10)
            .IsRequired();

        builder.Property(x => x.VideoFilePath)
            .HasColumnName("video_file_path")
            .HasMaxLength(1000)
            .IsRequired();

        builder.Property(x => x.ThumbnailFilePath)
            .HasColumnName("thumbnail_file_path")
            .HasMaxLength(1000);

        builder.Property(x => x.YoutubeVideoId)
            .HasColumnName("youtube_video_id")
            .HasMaxLength(128);

        builder.Property(x => x.YoutubeUrl)
            .HasColumnName("youtube_url")
            .HasMaxLength(500);

        builder.Property(x => x.ScheduledUploadAt)
            .HasColumnName("scheduled_upload_at");

        builder.Property(x => x.Attempts)
            .HasColumnName("attempts")
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(x => x.MaxAttempts)
            .HasColumnName("max_attempts")
            .HasDefaultValue(5)
            .IsRequired();

        builder.Property(x => x.LastError)
            .HasColumnName("last_error")
            .HasColumnType("text");

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired()
            .HasDefaultValueSql("NOW()");

        builder.Property(x => x.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired()
            .HasDefaultValueSql("NOW()");

        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.ScheduledUploadAt);
        builder.HasIndex(x => x.Priority);
        builder.HasIndex(x => new { x.Status, x.ScheduledUploadAt, x.Priority });
    }
}
