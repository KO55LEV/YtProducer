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
            .ValueGeneratedNever();

        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(x => x.Priority)
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(x => x.Title)
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(x => x.Description)
            .HasMaxLength(5000);

        builder.Property(x => x.Tags)
            .HasColumnType("text[]");

        builder.Property(x => x.CategoryId)
            .HasDefaultValue(10)
            .IsRequired();

        builder.Property(x => x.VideoFilePath)
            .HasMaxLength(1000)
            .IsRequired();

        builder.Property(x => x.ThumbnailFilePath)
            .HasMaxLength(1000);

        builder.Property(x => x.YoutubeVideoId)
            .HasMaxLength(128);

        builder.Property(x => x.YoutubeUrl)
            .HasMaxLength(500);

        builder.Property(x => x.ScheduledUploadAt);

        builder.Property(x => x.Attempts)
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(x => x.MaxAttempts)
            .HasDefaultValue(5)
            .IsRequired();

        builder.Property(x => x.LastError)
            .HasColumnType("text");

        builder.Property(x => x.CreatedAt)
            .IsRequired()
            .HasDefaultValueSql("NOW()");

        builder.Property(x => x.UpdatedAt)
            .IsRequired()
            .HasDefaultValueSql("NOW()");

        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.ScheduledUploadAt);
        builder.HasIndex(x => x.Priority);
        builder.HasIndex(x => new { x.Status, x.ScheduledUploadAt, x.Priority });
    }
}
