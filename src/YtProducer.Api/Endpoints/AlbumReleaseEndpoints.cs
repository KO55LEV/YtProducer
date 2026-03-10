using System.Text.Json;
using System.Text.RegularExpressions;
using System.Diagnostics;
using System.Globalization;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using YtProducer.Contracts.AlbumReleases;
using YtProducer.Contracts.Jobs;
using YtProducer.Domain.Entities;
using YtProducer.Domain.Enums;
using YtProducer.Infrastructure.Persistence;
using YtProducer.Infrastructure.Services;

namespace YtProducer.Api.Endpoints;

public static class AlbumReleaseEndpoints
{
    public static IEndpointRouteBuilder MapAlbumReleaseEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/playlists/{playlistId:guid}/album-release").WithTags("AlbumRelease");

        group.MapGet(string.Empty, GetOrCreateAlbumReleaseAsync)
            .WithName("GetOrCreateAlbumRelease")
            .Produces<AlbumReleaseResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPut(string.Empty, UpdateAlbumReleaseAsync)
            .WithName("UpdateAlbumRelease")
            .Produces<AlbumReleaseResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/regenerate-thumbnail", RegenerateAlbumReleaseThumbnailAsync)
            .WithName("RegenerateAlbumReleaseThumbnail")
            .Produces<AlbumReleaseResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/generate-assets", ScheduleGenerateAlbumReleaseAssetsAsync)
            .WithName("ScheduleGenerateAlbumReleaseAssets")
            .Produces<ScheduleAlbumReleaseJobResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/upload-youtube", ScheduleUploadAlbumReleaseToYoutubeAsync)
            .WithName("ScheduleUploadAlbumReleaseToYoutube")
            .Produces<ScheduleAlbumReleaseJobResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status400BadRequest);

        group.MapPost("/delete-temp-files", ScheduleDeleteAlbumReleaseTempFilesAsync)
            .WithName("ScheduleDeleteAlbumReleaseTempFiles")
            .Produces<ScheduleDeleteAlbumReleaseTempFilesResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/media/{fileName}", GetAlbumReleaseMediaFileAsync)
            .WithName("GetAlbumReleaseMediaFile")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> GetOrCreateAlbumReleaseAsync(
        Guid playlistId,
        YtProducerDbContext dbContext,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var playlist = await dbContext.Playlists
            .Include(x => x.Tracks)
            .FirstOrDefaultAsync(x => x.Id == playlistId, cancellationToken);
        if (playlist == null)
        {
            return Results.NotFound(new { message = $"Playlist {playlistId} not found" });
        }

        var release = await dbContext.AlbumReleases
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(x => x.PlaylistId == playlistId, cancellationToken);

        if (release == null)
        {
            release = await CreateAlbumReleaseDraftAsync(playlist, configuration, cancellationToken);
            dbContext.AlbumReleases.Add(release);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        else if (release.Status == AlbumReleaseStatus.Draft && ContainsMalformedAlbumTracklist(release.Description))
        {
            release.Description = await BuildAlbumReleaseDescriptionAsync(playlist, ResolvePlaylistRoot(playlist.Id, configuration), cancellationToken);
            release.UpdatedAtUtc = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return Results.Ok(await MapAlbumReleaseResponseAsync(release, playlist, configuration, cancellationToken));
    }

    private static async Task<IResult> UpdateAlbumReleaseAsync(
        Guid playlistId,
        UpdateAlbumReleaseRequest request,
        YtProducerDbContext dbContext,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var release = await dbContext.AlbumReleases
            .FirstOrDefaultAsync(x => x.PlaylistId == playlistId, cancellationToken);
        if (release == null)
        {
            return Results.NotFound(new { message = $"Album release for playlist {playlistId} not found" });
        }

        var playlist = await dbContext.Playlists
            .Include(x => x.Tracks)
            .FirstOrDefaultAsync(x => x.Id == playlistId, cancellationToken);
        if (playlist == null)
        {
            return Results.NotFound(new { message = $"Playlist {playlistId} not found" });
        }

        release.Title = string.IsNullOrWhiteSpace(request.Title) ? release.Title : request.Title.Trim();
        release.Description = request.Description?.Trim();
        release.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.Ok(await MapAlbumReleaseResponseAsync(release, playlist, configuration, cancellationToken));
    }

    private static async Task<IResult> RegenerateAlbumReleaseThumbnailAsync(
        Guid playlistId,
        YtProducerDbContext dbContext,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var release = await dbContext.AlbumReleases
            .FirstOrDefaultAsync(x => x.PlaylistId == playlistId, cancellationToken);
        if (release == null)
        {
            return Results.NotFound(new { message = $"Album release for playlist {playlistId} not found" });
        }

        var playlist = await dbContext.Playlists
            .Include(x => x.Tracks)
            .FirstOrDefaultAsync(x => x.Id == playlistId, cancellationToken);
        if (playlist == null)
        {
            return Results.NotFound(new { message = $"Playlist {playlistId} not found" });
        }

        var metadata = ParseAlbumReleaseMetadata(release.Metadata);
        metadata = metadata with { ThumbnailVersion = metadata.ThumbnailVersion + 1 };
        release.Metadata = JsonSerializer.Serialize(metadata);
        release.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.Ok(await MapAlbumReleaseResponseAsync(release, playlist, configuration, cancellationToken));
    }

    private static async Task<IResult> ScheduleDeleteAlbumReleaseTempFilesAsync(
        Guid playlistId,
        YtProducerDbContext dbContext,
        IJobService jobService,
        CancellationToken cancellationToken)
    {
        var release = await dbContext.AlbumReleases
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.PlaylistId == playlistId, cancellationToken);
        if (release == null)
        {
            return Results.NotFound(new { message = $"Album release for playlist {playlistId} not found" });
        }

        var payloadArguments = new CreateDeleteAlbumReleaseTempFilesJobArguments(release.Id);
        var payloadJson = JsonSerializer.Serialize(new ScheduledCommandPayload(
            "delete-album-release-temp-files",
            1,
            JsonSerializer.SerializeToElement(payloadArguments)));

        var result = await jobService.CreateAsync(new Job
        {
            Type = JobType.DeleteAlbumReleaseTempFiles,
            TargetType = "album_release",
            TargetId = release.Id,
            PayloadJson = payloadJson,
            MaxRetries = 3
        }, cancellationToken);

        var response = new ScheduleDeleteAlbumReleaseTempFilesResponse(
            release.Id,
            result.Job.Id,
            result.Job.Type.ToString(),
            result.Job.Status.ToString());

        return result.CreatedNew
            ? Results.Created($"/jobs/{result.Job.Id}", response)
            : Results.Ok(response);
    }

    private static async Task<IResult> ScheduleGenerateAlbumReleaseAssetsAsync(
        Guid playlistId,
        YtProducerDbContext dbContext,
        IJobService jobService,
        CancellationToken cancellationToken)
    {
        var release = await dbContext.AlbumReleases
            .FirstOrDefaultAsync(x => x.PlaylistId == playlistId, cancellationToken);
        if (release == null)
        {
            return Results.NotFound(new { message = $"Album release for playlist {playlistId} not found" });
        }

        release.Status = AlbumReleaseStatus.Preparing;
        release.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        var payloadArguments = new CreateGenerateAlbumReleaseAssetsJobArguments(release.Id);
        var payloadJson = JsonSerializer.Serialize(new ScheduledCommandPayload(
            "generate-album-release-assets",
            1,
            JsonSerializer.SerializeToElement(payloadArguments)));

        var result = await jobService.CreateAsync(new Job
        {
            Type = JobType.GenerateAlbumReleaseAssets,
            TargetType = "album_release",
            TargetId = release.Id,
            PayloadJson = payloadJson,
            MaxRetries = 3
        }, cancellationToken);

        return BuildScheduledAlbumReleaseResponse(
            result,
            new ScheduleAlbumReleaseJobResponse(release.Id, result.Job.Id, result.Job.Type.ToString(), result.Job.Status.ToString()));
    }

    private static async Task<IResult> ScheduleUploadAlbumReleaseToYoutubeAsync(
        Guid playlistId,
        YtProducerDbContext dbContext,
        IJobService jobService,
        CancellationToken cancellationToken)
    {
        var release = await dbContext.AlbumReleases
            .FirstOrDefaultAsync(x => x.PlaylistId == playlistId, cancellationToken);
        if (release == null)
        {
            return Results.NotFound(new { message = $"Album release for playlist {playlistId} not found" });
        }

        if (string.IsNullOrWhiteSpace(release.OutputVideoPath))
        {
            return Results.BadRequest(new { message = "Generate album release assets first." });
        }

        release.Status = AlbumReleaseStatus.Uploading;
        release.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        var payloadArguments = new CreateUploadAlbumReleaseToYoutubeJobArguments(release.Id);
        var payloadJson = JsonSerializer.Serialize(new ScheduledCommandPayload(
            "upload-album-release-youtube",
            1,
            JsonSerializer.SerializeToElement(payloadArguments)));

        var result = await jobService.CreateAsync(new Job
        {
            Type = JobType.UploadAlbumReleaseToYoutube,
            TargetType = "album_release",
            TargetId = release.Id,
            PayloadJson = payloadJson,
            MaxRetries = 3
        }, cancellationToken);

        return BuildScheduledAlbumReleaseResponse(
            result,
            new ScheduleAlbumReleaseJobResponse(release.Id, result.Job.Id, result.Job.Type.ToString(), result.Job.Status.ToString()));
    }

    private static IResult GetAlbumReleaseMediaFileAsync(
        Guid playlistId,
        string fileName,
        YtProducerDbContext dbContext,
        IConfiguration configuration)
    {
        var release = dbContext.AlbumReleases
            .AsNoTracking()
            .FirstOrDefault(x => x.PlaylistId == playlistId);
        if (release == null || string.IsNullOrWhiteSpace(release.TempRootPath))
        {
            return Results.NotFound();
        }

        var safeFileName = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(safeFileName))
        {
            return Results.NotFound();
        }

        var configuredRoot = ResolveAlbumReleaseTempRoot(playlistId, configuration);
        var tempRoot = Path.GetFullPath(release.TempRootPath);
        var expectedRoot = Path.GetFullPath(configuredRoot);
        if (!tempRoot.StartsWith(expectedRoot, StringComparison.OrdinalIgnoreCase))
        {
            return Results.NotFound();
        }

        var candidatePath = Path.GetFullPath(Path.Combine(tempRoot, safeFileName));
        if (!candidatePath.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase) || !File.Exists(candidatePath))
        {
            return Results.NotFound();
        }

        var provider = new FileExtensionContentTypeProvider();
        if (!provider.TryGetContentType(candidatePath, out var contentType))
        {
            contentType = "application/octet-stream";
        }

        return Results.File(candidatePath, contentType);
    }

    private static async Task<AlbumRelease> CreateAlbumReleaseDraftAsync(Playlist playlist, IConfiguration configuration, CancellationToken cancellationToken)
    {
        var playlistRoot = ResolvePlaylistRoot(playlist.Id, configuration);
        var title = $"{playlist.Title} | Full Album";
        var description = await BuildAlbumReleaseDescriptionAsync(playlist, playlistRoot, cancellationToken);
        return new AlbumRelease
        {
            Id = Guid.NewGuid(),
            PlaylistId = playlist.Id,
            Status = AlbumReleaseStatus.Draft,
            Title = title,
            Description = description,
            TempRootPath = ResolveAlbumReleaseTempRoot(playlist.Id, configuration),
            Metadata = JsonSerializer.Serialize(new AlbumReleaseMetadata())
        };
    }

    private static async Task<AlbumReleaseResponse> MapAlbumReleaseResponseAsync(
        AlbumRelease release,
        Playlist playlist,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();

        var playlistRoot = ResolvePlaylistRoot(playlist.Id, configuration);
        var metadata = ParseAlbumReleaseMetadata(release.Metadata);
        var mediaByPosition = DiscoverPlaylistMedia(playlistRoot);
        var tracks = await BuildAlbumReleaseTracksAsync(playlist, mediaByPosition, cancellationToken);
        var previewUrls = BuildThumbnailPreviewUrls(metadata.ThumbnailVersion, tracks);
        var totalDurationSeconds = tracks.Sum(x => x.DurationSeconds);
        var tempFilesExist = !string.IsNullOrWhiteSpace(release.TempRootPath) && Directory.Exists(release.TempRootPath);
        var tempFileCount = tempFilesExist
            ? Directory.EnumerateFiles(release.TempRootPath!, "*", SearchOption.AllDirectories).Count()
            : 0;
        var thumbnailUrl = BuildAlbumReleaseMediaUrl(playlist.Id, release.ThumbnailPath);
        var outputVideoUrl = BuildAlbumReleaseMediaUrl(playlist.Id, release.OutputVideoPath);

        return new AlbumReleaseResponse(
            release.Id,
            release.PlaylistId,
            release.Status.ToString(),
            release.Title ?? $"{playlist.Title} | Full Album",
            release.Description,
            release.ThumbnailPath,
            thumbnailUrl,
            release.OutputVideoPath,
            outputVideoUrl,
            release.TempRootPath,
            release.YoutubeVideoId,
            release.YoutubeUrl,
            tempFilesExist,
            tempFileCount,
            tracks.Count,
            totalDurationSeconds,
            metadata.ThumbnailVersion,
            previewUrls,
            tracks,
            release.Metadata,
            release.CreatedAtUtc,
            release.UpdatedAtUtc,
            release.FinishedAtUtc);
    }

    private static IResult BuildScheduledAlbumReleaseResponse<TResponse>(JobCreateResult result, TResponse response)
    {
        return result.CreatedNew
            ? Results.Created($"/jobs/{result.Job.Id}", response)
            : Results.Ok(response);
    }

    private static async Task<List<AlbumReleaseTrackResponse>> BuildAlbumReleaseTracksAsync(
        Playlist playlist,
        IReadOnlyDictionary<int, PlaylistMediaBundle> mediaByPosition,
        CancellationToken cancellationToken)
    {
        var offsetSeconds = 0d;
        var results = new List<AlbumReleaseTrackResponse>();

        foreach (var track in playlist.Tracks.OrderBy(x => x.PlaylistPosition))
        {
            cancellationToken.ThrowIfCancellationRequested();
            mediaByPosition.TryGetValue(track.PlaylistPosition, out var media);
            var durationSeconds = await ResolveTrackDurationSecondsAsync(track, media, cancellationToken) ?? 0d;
            results.Add(new AlbumReleaseTrackResponse(
                track.Id,
                track.PlaylistPosition,
                track.Title,
                track.Duration,
                durationSeconds,
                offsetSeconds,
                FormatTimestamp(offsetSeconds),
                media?.Images.FirstOrDefault()?.Url,
                media?.Videos.FirstOrDefault()?.Url));
            offsetSeconds += durationSeconds;
        }

        return results;
    }

    private static IReadOnlyList<string> BuildThumbnailPreviewUrls(
        int thumbnailVersion,
        IReadOnlyList<AlbumReleaseTrackResponse> tracks)
    {
        var pool = tracks
            .Select(x => x.PreviewImageUrl)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (pool.Count == 0)
        {
            return Array.Empty<string>();
        }

        var count = Math.Min(4, pool.Count);
        var start = thumbnailVersion % pool.Count;
        var result = new List<string>(count);
        for (var index = 0; index < count; index++)
        {
            result.Add(pool[(start + index) % pool.Count]);
        }

        return result;
    }

    private static async Task<string> BuildAlbumReleaseDescriptionAsync(Playlist playlist, string? playlistRoot, CancellationToken cancellationToken)
    {
        var mediaByPosition = DiscoverPlaylistMedia(playlistRoot);
        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(playlist.Description))
        {
            lines.Add(playlist.Description.Trim());
            lines.Add(string.Empty);
        }

        lines.Add("Tracklist:");
        var offsetSeconds = 0d;
        foreach (var track in playlist.Tracks.OrderBy(x => x.PlaylistPosition))
        {
            cancellationToken.ThrowIfCancellationRequested();
            lines.Add($"{FormatTimestamp(offsetSeconds)} - {track.Title}");
            mediaByPosition.TryGetValue(track.PlaylistPosition, out var media);
            offsetSeconds += await ResolveTrackDurationSecondsAsync(track, media, cancellationToken) ?? 0d;
        }

        return string.Join(Environment.NewLine, lines).Trim();
    }

    private static string ResolveAlbumReleaseTempRoot(Guid playlistId, IConfiguration configuration)
    {
        var explicitRoot = Environment.GetEnvironmentVariable("YT_PRODUCER_ALBUM_RELEASE_TEMP_ROOT")
            ?? configuration["YT_PRODUCER_ALBUM_RELEASE_TEMP_ROOT"];
        if (!string.IsNullOrWhiteSpace(explicitRoot))
        {
            return Path.Combine(explicitRoot, playlistId.ToString());
        }

        var workingDirectory = Environment.GetEnvironmentVariable("YT_PRODUCER_PLAYLIST_WORKING_DIRECTORY")
            ?? configuration["YT_PRODUCER_PLAYLIST_WORKING_DIRECTORY"]
            ?? ".";
        return Path.Combine(workingDirectory, "tmp", "album-releases", playlistId.ToString());
    }

    private static string? BuildAlbumReleaseMediaUrl(Guid playlistId, string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return null;
        }

        var fileName = Path.GetFileName(filePath);
        return string.IsNullOrWhiteSpace(fileName)
            ? null
            : $"/playlists/{playlistId}/album-release/media/{fileName}";
    }

    private static string? ResolvePlaylistRoot(Guid playlistId, IConfiguration configuration)
    {
        var workingDirectory = Environment.GetEnvironmentVariable("YT_PRODUCER_PLAYLIST_WORKING_DIRECTORY")
            ?? configuration["YT_PRODUCER_PLAYLIST_WORKING_DIRECTORY"];
        if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            return null;
        }

        var playlistRoot = Path.Combine(workingDirectory, playlistId.ToString());
        return Directory.Exists(playlistRoot) ? playlistRoot : null;
    }

    private static Dictionary<int, PlaylistMediaBundle> DiscoverPlaylistMedia(string? playlistRoot)
    {
        var result = new Dictionary<int, PlaylistMediaBundle>();
        if (string.IsNullOrWhiteSpace(playlistRoot) || !Directory.Exists(playlistRoot))
        {
            return result;
        }

        foreach (var filePath in Directory.EnumerateFiles(playlistRoot))
        {
            var fileName = Path.GetFileName(filePath);
            if (string.IsNullOrWhiteSpace(fileName) || fileName.StartsWith("._", StringComparison.Ordinal))
            {
                continue;
            }

            var position = ParsePlaylistPosition(Path.GetFileNameWithoutExtension(fileName));
            if (position == null)
            {
                continue;
            }

            if (!result.TryGetValue(position.Value, out var bucket))
            {
                bucket = new PlaylistMediaBundle();
                result[position.Value] = bucket;
            }

            var extension = Path.GetExtension(fileName);
            var mediaUrl = $"/playlists/{Path.GetFileName(playlistRoot)}/media/{fileName}";
            if (new[] { ".jpg", ".jpeg", ".png", ".webp" }.Contains(extension, StringComparer.OrdinalIgnoreCase))
            {
                bucket.Images.Add(new PlaylistMediaFile(filePath, mediaUrl));
            }
            else if (string.Equals(extension, ".mp4", StringComparison.OrdinalIgnoreCase))
            {
                bucket.Videos.Add(new PlaylistMediaFile(filePath, mediaUrl));
            }
            else if (string.Equals(extension, ".mp3", StringComparison.OrdinalIgnoreCase))
            {
                bucket.Audios.Add(new PlaylistMediaFile(filePath, mediaUrl));
            }
        }

        return result;
    }

    private static int? ParsePlaylistPosition(string baseName)
    {
        var thumbnailMatch = Regex.Match(baseName, "^(?<pos>\\d+)_thumbnail(?:_\\d+)?$", RegexOptions.CultureInvariant);
        if (thumbnailMatch.Success && int.TryParse(thumbnailMatch.Groups["pos"].Value, out var thumbnailPosition))
        {
            return thumbnailPosition;
        }

        var match = Regex.Match(baseName, "^(?<pos>\\d+)(?:_\\d+)?$", RegexOptions.CultureInvariant);
        if (!match.Success)
        {
            return null;
        }

        return int.TryParse(match.Groups["pos"].Value, out var position) ? position : null;
    }

    private static double? ParseTrackDurationSeconds(string? duration)
    {
        if (string.IsNullOrWhiteSpace(duration))
        {
            return null;
        }

        var parts = duration.Split(':', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 2 &&
            int.TryParse(parts[0], out var minutes) &&
            int.TryParse(parts[1], out var seconds))
        {
            return (minutes * 60) + seconds;
        }

        if (parts.Length == 3 &&
            int.TryParse(parts[0], out var hours) &&
            int.TryParse(parts[1], out minutes) &&
            int.TryParse(parts[2], out seconds))
        {
            return (hours * 3600) + (minutes * 60) + seconds;
        }

        if (TimeSpan.TryParse(duration, out var parsed))
        {
            return parsed.TotalSeconds;
        }

        return null;
    }

    private static async Task<double?> ResolveTrackDurationSecondsAsync(
        Track track,
        PlaylistMediaBundle? media,
        CancellationToken cancellationToken)
    {
        var videoPath = media?.Videos.FirstOrDefault()?.Path;
        var audioPath = media?.Audios.FirstOrDefault()?.Path;

        return await ProbeMediaDurationSecondsAsync(videoPath, cancellationToken)
            ?? await ProbeMediaDurationSecondsAsync(audioPath, cancellationToken)
            ?? ParseTrackDurationSeconds(track.Duration);
    }

    private static async Task<double?> ProbeMediaDurationSecondsAsync(string? mediaPath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(mediaPath) || !File.Exists(mediaPath))
        {
            return null;
        }

        var ffprobePath = Environment.GetEnvironmentVariable("FFPROBE_PATH") ?? "ffprobe";
        var startInfo = new ProcessStartInfo
        {
            FileName = ffprobePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-v");
        startInfo.ArgumentList.Add("error");
        startInfo.ArgumentList.Add("-show_entries");
        startInfo.ArgumentList.Add("format=duration");
        startInfo.ArgumentList.Add("-of");
        startInfo.ArgumentList.Add("default=noprint_wrappers=1:nokey=1");
        startInfo.ArgumentList.Add(mediaPath);

        using var process = new Process { StartInfo = startInfo };
        process.Start();
        var stdOut = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            return null;
        }

        return double.TryParse(stdOut.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static string FormatTimestamp(double totalSeconds)
    {
        var safe = Math.Max(0, (int)Math.Round(totalSeconds));
        var minutes = safe / 60;
        var seconds = safe % 60;
        return $"{minutes}:{seconds:00}";
    }

    private static AlbumReleaseMetadata ParseAlbumReleaseMetadata(string? metadata)
    {
        if (string.IsNullOrWhiteSpace(metadata))
        {
            return new AlbumReleaseMetadata();
        }

        try
        {
            return JsonSerializer.Deserialize<AlbumReleaseMetadata>(metadata) ?? new AlbumReleaseMetadata();
        }
        catch (JsonException)
        {
            return new AlbumReleaseMetadata();
        }
    }

    private static bool ContainsMalformedAlbumTracklist(string? description)
    {
        return !string.IsNullOrWhiteSpace(description)
            && Regex.IsMatch(description, "\\b\\d{5,}:\\d{2}\\b", RegexOptions.CultureInvariant);
    }

    private sealed class PlaylistMediaBundle
    {
        public List<PlaylistMediaFile> Images { get; } = [];
        public List<PlaylistMediaFile> Videos { get; } = [];
        public List<PlaylistMediaFile> Audios { get; } = [];
    }

    private sealed record PlaylistMediaFile(string Path, string Url);

    private sealed record AlbumReleaseMetadata(int ThumbnailVersion = 0);
}
