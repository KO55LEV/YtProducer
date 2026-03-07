using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using YtProducer.Domain.Entities;

namespace YtProducer.Infrastructure.Persistence.Configurations;

public sealed class TrackVideoGenerationConfiguration : IEntityTypeConfiguration<TrackVideoGeneration>
{
    public void Configure(EntityTypeBuilder<TrackVideoGeneration> builder)
    {
        builder.ToTable("track_video_generation");

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

        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(x => x.ProgressPercent)
            .HasColumnName("progress_percent")
            .IsRequired();

        builder.Property(x => x.ProgressCurrentFrame)
            .HasColumnName("progress_current_frame");

        builder.Property(x => x.ProgressTotalFrames)
            .HasColumnName("progress_total_frames");

        builder.Property(x => x.TrackDurationSeconds)
            .HasColumnName("track_duration_seconds");

        builder.Property(x => x.ImagePath)
            .HasColumnName("image_path")
            .HasMaxLength(2000);

        builder.Property(x => x.AudioPath)
            .HasColumnName("audio_path")
            .HasMaxLength(2000);

        builder.Property(x => x.TempDir)
            .HasColumnName("temp_dir")
            .HasMaxLength(2000);

        builder.Property(x => x.OutputDir)
            .HasColumnName("output_dir")
            .HasMaxLength(2000);

        builder.Property(x => x.Width)
            .HasColumnName("width");

        builder.Property(x => x.Height)
            .HasColumnName("height");

        builder.Property(x => x.Fps)
            .HasColumnName("fps");

        builder.Property(x => x.EqBands)
            .HasColumnName("eq_bands");

        builder.Property(x => x.VideoBitrate)
            .HasColumnName("video_bitrate")
            .HasMaxLength(32);

        builder.Property(x => x.AudioBitrate)
            .HasColumnName("audio_bitrate")
            .HasMaxLength(32);

        builder.Property(x => x.Seed)
            .HasColumnName("seed");

        builder.Property(x => x.UseGpu)
            .HasColumnName("use_gpu");

        builder.Property(x => x.KeepTemp)
            .HasColumnName("keep_temp");

        builder.Property(x => x.UseRawPipe)
            .HasColumnName("use_raw_pipe");

        builder.Property(x => x.RendererVariant)
            .HasColumnName("renderer_variant")
            .HasMaxLength(32);

        builder.Property(x => x.OutputFileNameOverride)
            .HasColumnName("output_file_name_override")
            .HasMaxLength(256);

        builder.Property(x => x.LogoPath)
            .HasColumnName("logo_path")
            .HasMaxLength(2000);

        builder.Property(x => x.OutputVideoPath)
            .HasColumnName("output_video_path")
            .HasMaxLength(2000);

        builder.Property(x => x.AnalysisPath)
            .HasColumnName("analysis_path")
            .HasMaxLength(2000);

        builder.Property(x => x.FfmpegCommand)
            .HasColumnName("ffmpeg_command")
            .HasColumnType("text");

        builder.Property(x => x.ErrorMessage)
            .HasColumnName("error_message")
            .HasColumnType("text");

        builder.Property(x => x.Metadata)
            .HasColumnName("metadata")
            .HasColumnType("jsonb");

        builder.Property(x => x.StartedAtUtc)
            .HasColumnName("started_at_utc");

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

        builder.HasIndex(x => x.TrackId).IsUnique();
        builder.HasIndex(x => x.PlaylistId);
        builder.HasIndex(x => new { x.PlaylistId, x.PlaylistPosition });
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.UpdatedAtUtc);
    }
}
