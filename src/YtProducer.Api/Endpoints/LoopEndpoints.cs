using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using YtProducer.Contracts.Jobs;
using YtProducer.Contracts.Loops;
using YtProducer.Domain.Entities;
using YtProducer.Domain.Enums;
using YtProducer.Infrastructure.Persistence;
using YtProducer.Infrastructure.Services;

namespace YtProducer.Api.Endpoints;

public static class LoopEndpoints
{
    public static IEndpointRouteBuilder MapLoopEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/track-loops").WithTags("Track Loops");

        group.MapPost("/schedule-by-youtube-video", ScheduleTrackLoopByYoutubeVideoAsync)
            .WithName("ScheduleTrackLoopByYoutubeVideo")
            .Produces<ScheduleTrackLoopResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> ScheduleTrackLoopByYoutubeVideoAsync(
        CreateTrackLoopByYoutubeVideoRequest request,
        YtProducerDbContext dbContext,
        IJobService jobService,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.YoutubeVideoId))
        {
            return Results.BadRequest(new { message = "youtubeVideoId is required" });
        }

        if (request.LoopCount < 2)
        {
            return Results.BadRequest(new { message = "loopCount must be 2 or greater" });
        }

        var youtubeVideoId = request.YoutubeVideoId.Trim();
        var youtubeRecord = await dbContext.TrackOnYoutube
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.VideoId == youtubeVideoId, cancellationToken);

        if (youtubeRecord is null)
        {
            return Results.NotFound(new { message = $"YouTube video {youtubeVideoId} not found" });
        }

        var playlist = await dbContext.Playlists
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == youtubeRecord.PlaylistId, cancellationToken);

        if (playlist is null)
        {
            return Results.NotFound(new { message = $"Playlist {youtubeRecord.PlaylistId} not found" });
        }

        var track = await dbContext.Tracks
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == youtubeRecord.TrackId && x.PlaylistId == youtubeRecord.PlaylistId, cancellationToken);

        if (track is null)
        {
            return Results.NotFound(new { message = $"Track {youtubeRecord.TrackId} not found" });
        }

        var workingDirectory = Environment.GetEnvironmentVariable("YT_PRODUCER_PLAYLIST_WORKING_DIRECTORY")
            ?? configuration["YT_PRODUCER_PLAYLIST_WORKING_DIRECTORY"];
        var playlistRoot = string.IsNullOrWhiteSpace(workingDirectory)
            ? null
            : Path.Combine(workingDirectory, playlist.Id.ToString());

        var now = DateTimeOffset.UtcNow;
        var loop = new TrackLoop
        {
            Id = Guid.NewGuid(),
            PlaylistId = playlist.Id,
            TrackId = track.Id,
            TrackPosition = track.PlaylistPosition,
            LoopCount = request.LoopCount,
            Status = TrackLoopStatus.Pending,
            SourceAudioPath = ResolvePreferredMediaFile(playlistRoot, track.PlaylistPosition, [".mp3"]),
            SourceImagePath = ResolvePreferredMediaFile(playlistRoot, track.PlaylistPosition, [".jpg", ".jpeg", ".png", ".webp"]),
            SourceVideoPath = ResolvePreferredMediaFile(playlistRoot, track.PlaylistPosition, [".mp4", ".mov", ".webm"]),
            ThumbnailPath = ResolvePreferredMediaFile(playlistRoot, track.PlaylistPosition, [".jpg", ".jpeg", ".png", ".webp"], "_thumbnail"),
            Title = track.Title,
            Description = track.YouTubeTitle,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        dbContext.TrackLoops.Add(loop);
        await dbContext.SaveChangesAsync(cancellationToken);

        var payloadArguments = new CreateTrackLoopJobArguments(
            loop.Id,
            loop.PlaylistId,
            loop.TrackId,
            loop.TrackPosition,
            loop.LoopCount);

        var payloadJson = JsonSerializer.Serialize(new ScheduledCommandPayload(
            "create-track-loop",
            1,
            JsonSerializer.SerializeToElement(payloadArguments)));

        var job = await jobService.CreateAsync(new Job
        {
            Type = JobType.CreateTrackLoop,
            TargetType = "track_loop",
            TargetId = loop.Id,
            PayloadJson = payloadJson,
            MaxRetries = 3
        }, cancellationToken);

        return Results.Created(
            $"/track-loops/{loop.Id}",
            new ScheduleTrackLoopResponse(job.Id, job.Type.ToString(), MapToTrackLoopResponse(loop)));
    }

    private static TrackLoopResponse MapToTrackLoopResponse(TrackLoop item)
    {
        return new TrackLoopResponse(
            item.Id,
            item.PlaylistId,
            item.TrackId,
            item.TrackPosition,
            item.LoopCount,
            item.Status.ToString(),
            item.SourceAudioPath,
            item.SourceImagePath,
            item.SourceVideoPath,
            item.OutputVideoPath,
            item.ThumbnailPath,
            item.YoutubeVideoId,
            item.YoutubeUrl,
            item.Title,
            item.Description,
            item.Metadata,
            item.StartedAtUtc,
            item.FinishedAtUtc,
            item.CreatedAtUtc,
            item.UpdatedAtUtc);
    }

    private static string? ResolvePreferredMediaFile(
        string? playlistRoot,
        int playlistPosition,
        IReadOnlyCollection<string> extensions,
        string? requiredNameSuffix = null)
    {
        if (string.IsNullOrWhiteSpace(playlistRoot) || !Directory.Exists(playlistRoot))
        {
            return null;
        }

        var normalizedExtensions = new HashSet<string>(extensions, StringComparer.OrdinalIgnoreCase);

        var candidates = Directory.EnumerateFiles(playlistRoot)
            .Where(path => normalizedExtensions.Contains(Path.GetExtension(path)))
            .Select(path => new
            {
                Path = path,
                Variant = GetMediaVariantOrder(Path.GetFileNameWithoutExtension(path), playlistPosition, requiredNameSuffix)
            })
            .Where(x => x.Variant.HasValue)
            .OrderBy(x => x.Variant!.Value)
            .ThenBy(x => x.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return candidates.Count == 0 ? null : candidates[0].Path;
    }

    private static int? GetMediaVariantOrder(string fileNameWithoutExtension, int playlistPosition, string? requiredNameSuffix)
    {
        var positionPrefix = playlistPosition.ToString();
        var expectedBase = string.IsNullOrWhiteSpace(requiredNameSuffix)
            ? positionPrefix
            : $"{positionPrefix}{requiredNameSuffix}";

        if (fileNameWithoutExtension.Equals(expectedBase, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        var escapedSuffix = string.IsNullOrWhiteSpace(requiredNameSuffix)
            ? string.Empty
            : System.Text.RegularExpressions.Regex.Escape(requiredNameSuffix);
        var match = System.Text.RegularExpressions.Regex.Match(
            fileNameWithoutExtension,
            $"^{System.Text.RegularExpressions.Regex.Escape(positionPrefix)}{escapedSuffix}_(\\d+)$",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant | System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        if (!match.Success)
        {
            return null;
        }

        return int.TryParse(match.Groups[1].Value, out var parsed) ? parsed : null;
    }
}
