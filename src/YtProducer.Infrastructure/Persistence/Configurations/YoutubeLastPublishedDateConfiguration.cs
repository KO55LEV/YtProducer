using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using YtProducer.Domain.Entities;

namespace YtProducer.Infrastructure.Persistence.Configurations;

public sealed class YoutubeLastPublishedDateConfiguration : IEntityTypeConfiguration<YoutubeLastPublishedDate>
{
    public void Configure(EntityTypeBuilder<YoutubeLastPublishedDate> builder)
    {
        builder.ToTable("youtube_last_published_date");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(x => x.LastPublishedDate)
            .HasColumnName("last_published_date")
            .IsRequired();

        builder.Property(x => x.VideoId)
            .HasColumnName("video_id")
            .HasMaxLength(64);
    }
}
