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
            .ValueGeneratedNever();

        builder.Property(x => x.PlaylistId)
            .IsRequired();

        builder.Property(x => x.PlaylistPosition)
            .IsRequired();

        builder.Property(x => x.Title)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.YouTubeTitle)
            .HasMaxLength(200);

        builder.Property(x => x.SourceUrl)
            .HasMaxLength(1000);

        builder.Property(x => x.Style)
            .HasMaxLength(100);

        builder.Property(x => x.Duration)
            .HasMaxLength(20);

        builder.Property(x => x.TempoBpm);

        builder.Property(x => x.Key)
            .HasMaxLength(50);

        builder.Property(x => x.EnergyLevel);

        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(x => x.Metadata)
            .HasColumnType("jsonb");

        builder.Property(x => x.CreatedAtUtc)
            .IsRequired()
            .HasDefaultValueSql("NOW()");

        builder.Property(x => x.UpdatedAtUtc)
            .IsRequired()
            .HasDefaultValueSql("NOW()");

        builder.HasIndex(x => new { x.PlaylistId, x.PlaylistPosition })
            .IsUnique();

        builder.HasIndex(x => x.PlaylistId);
        builder.HasIndex(x => x.Status);
    }
}
