using YtProducer.Contracts.Playlists;
using YtProducer.Contracts.Jobs;
using YtProducer.Contracts.Loops;
using YtProducer.Contracts.Tracks;
using YtProducer.Domain.Entities;
using YtProducer.Domain.Enums;
using YtProducer.Infrastructure.Services;
using YtProducer.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.StaticFiles;
using System.Text.RegularExpressions;
using System.Text.Json;

namespace YtProducer.Api.Endpoints;

public static class PlaylistEndpoints
{
    public static IEndpointRouteBuilder MapPlaylistEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/playlists").WithTags("Playlists");

        group.MapPost(string.Empty, CreatePlaylistAsync)
            .WithName("CreatePlaylist")
            .Produces<PlaylistResponse>(StatusCodes.Status201Created);

        group.MapGet(string.Empty, GetPlaylistsAsync)
            .WithName("GetPlaylists")
            .Produces<IReadOnlyList<PlaylistResponse>>(StatusCodes.Status200OK);

        group.MapGet("/{id:guid}", GetPlaylistByIdAsync)
            .WithName("GetPlaylistById")
            .Produces<PlaylistResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPut("/{id:guid}/status", UpdatePlaylistStatusAsync)
            .WithName("UpdatePlaylistStatus")
            .Produces<UpdatePlaylistStatusResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/start", SchedulePlaylistStartAsync)
            .WithName("SchedulePlaylistStart")
            .Produces<SchedulePlaylistStartResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/generate-images", SchedulePlaylistGenerateImagesAsync)
            .WithName("SchedulePlaylistGenerateImages")
            .Produces<SchedulePlaylistGenerateImagesResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/generate-music", SchedulePlaylistGenerateMusicAsync)
            .WithName("SchedulePlaylistGenerateMusic")
            .Produces<SchedulePlaylistGenerateMusicResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/generate-thumbnails", SchedulePlaylistGenerateThumbnailsAsync)
            .WithName("SchedulePlaylistGenerateThumbnails")
            .Produces<SchedulePlaylistGenerateThumbnailsResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/generate-videos", SchedulePlaylistGenerateVideosAsync)
            .WithName("SchedulePlaylistGenerateVideos")
            .Produces<SchedulePlaylistGenerateVideosResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/generate-youtube-playlist", SchedulePlaylistGenerateYoutubePlaylistAsync)
            .WithName("SchedulePlaylistGenerateYoutubePlaylist")
            .Produces<SchedulePlaylistGenerateYoutubePlaylistResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/upload-youtube-videos", SchedulePlaylistUploadYoutubeVideosAsync)
            .WithName("SchedulePlaylistUploadYoutubeVideos")
            .Produces<SchedulePlaylistUploadYoutubeVideosResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/add-youtube-videos-to-playlist", SchedulePlaylistAddYoutubeVideosToPlaylistAsync)
            .WithName("SchedulePlaylistAddYoutubeVideosToPlaylist")
            .Produces<SchedulePlaylistAddYoutubeVideosToPlaylistResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/generate-youtube-engagements", SchedulePlaylistGenerateYoutubeEngagementsAsync)
            .WithName("SchedulePlaylistGenerateYoutubeEngagements")
            .Produces<SchedulePlaylistGenerateYoutubeEngagementsResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/{id:guid}/media", GetPlaylistMediaAsync)
            .WithName("GetPlaylistMedia")
            .Produces<PlaylistMediaResponse>(StatusCodes.Status200OK);

        group.MapGet("/{id:guid}/music-prompts", GetPlaylistMusicPromptsAsync)
            .WithName("GetPlaylistMusicPrompts")
            .Produces<PlaylistPromptResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/{id:guid}/image-prompts", GetPlaylistImagePromptsAsync)
            .WithName("GetPlaylistImagePrompts")
            .Produces<PlaylistPromptResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/media/set-background", SetPlaylistTrackBackgroundAsync)
            .WithName("SetPlaylistTrackBackground")
            .Produces<SetPlaylistTrackBackgroundResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/media/move-image", MovePlaylistTrackImageAsync)
            .WithName("MovePlaylistTrackImage")
            .Produces<MovePlaylistTrackImageResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/media/delete-thumbnail", DeletePlaylistTrackThumbnailAsync)
            .WithName("DeletePlaylistTrackThumbnail")
            .Produces<DeletePlaylistTrackThumbnailResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/media/move-audio", MovePlaylistTrackAudioAsync)
            .WithName("MovePlaylistTrackAudio")
            .Produces<MovePlaylistTrackAudioResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/media/delete-audio", DeletePlaylistTrackAudioAsync)
            .WithName("DeletePlaylistTrackAudio")
            .Produces<DeletePlaylistTrackAudioResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/{id:guid}/media/{*relativePath}", GetPlaylistMediaFileAsync)
            .WithName("GetPlaylistMediaFile");

        group.MapGet("/{id:guid}/youtube-videos", GetPlaylistYoutubeVideosAsync)
            .WithName("GetPlaylistYoutubeVideos")
            .Produces<IReadOnlyList<TrackOnYoutubeResponse>>(StatusCodes.Status200OK);

        group.MapGet("/{id:guid}/video-generations", GetPlaylistTrackVideoGenerationsAsync)
            .WithName("GetPlaylistTrackVideoGenerations")
            .Produces<IReadOnlyList<TrackVideoGenerationResponse>>(StatusCodes.Status200OK);

        group.MapGet("/{id:guid}/video-generations/{position:int}", GetPlaylistTrackVideoGenerationByPositionAsync)
            .WithName("GetPlaylistTrackVideoGenerationByPosition")
            .Produces<TrackVideoGenerationResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPut("/{id:guid}/video-generations/{position:int}", UpsertPlaylistTrackVideoGenerationAsync)
            .WithName("UpsertPlaylistTrackVideoGeneration")
            .Produces<TrackVideoGenerationResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/track-loops", ScheduleTrackLoopAsync)
            .WithName("ScheduleTrackLoop")
            .Produces<ScheduleTrackLoopResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/tracks/{trackId:guid}/reaction", AddTrackReactionAsync)
            .WithName("AddTrackReaction")
            .Produces<TrackSocialStatResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> CreatePlaylistAsync(
        CreatePlaylistRequest request,
        IPlaylistRepository repository,
        CancellationToken cancellationToken)
    {
        var validationError = ValidateCreatePlaylistRequest(request);
        if (validationError != null)
        {
            return Results.BadRequest(new { message = validationError });
        }

        var playlist = new Playlist
        {
            Title = request.Title,
            Theme = request.Theme,
            Description = request.Description,
            PlaylistStrategy = request.PlaylistStrategy,
            Metadata = request.Metadata,
            Status = PlaylistStatus.Draft,
            TrackCount = request.Tracks?.Length ?? 0,
            Tracks = request.Tracks?.Select(t => new Track
            {
                PlaylistPosition = t.PlaylistPosition,
                Title = t.Title,
                YouTubeTitle = t.YouTubeTitle,
                Style = t.Style,
                Duration = t.Duration,
                TempoBpm = t.TempoBpm,
                Key = t.Key,
                EnergyLevel = t.EnergyLevel,
                Metadata = t.Metadata,
                Status = TrackStatus.Pending
            }).ToList() ?? new List<Track>()
        };

        var created = await repository.CreateAsync(playlist, cancellationToken);

        var response = MapToPlaylistResponse(created);
        return Results.Created($"/playlists/{response.Id}", response);
    }

    private static string? ValidateCreatePlaylistRequest(CreatePlaylistRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
        {
            return "Playlist title is required.";
        }

        if (request.Tracks == null || request.Tracks.Length == 0)
        {
            return "Playlist must contain at least one track.";
        }

        var seenPositions = new HashSet<int>();

        for (var index = 0; index < request.Tracks.Length; index++)
        {
            var track = request.Tracks[index];

            if (track.PlaylistPosition <= 0)
            {
                return $"Track {index + 1} has an invalid playlist position.";
            }

            if (!seenPositions.Add(track.PlaylistPosition))
            {
                return $"Duplicate playlist position {track.PlaylistPosition} detected.";
            }

            if (string.IsNullOrWhiteSpace(track.Title))
            {
                return $"Track {track.PlaylistPosition} title is required.";
            }

            if (string.IsNullOrWhiteSpace(track.Metadata))
            {
                return $"Track {track.PlaylistPosition} metadata is required.";
            }
        }

        return null;
    }

    private static async Task<IResult> GetPlaylistsAsync(
        IPlaylistRepository repository,
        CancellationToken cancellationToken)
    {
        var playlists = await repository.GetAllAsync(cancellationToken);
        var response = playlists.Select(MapToPlaylistResponse).ToList();
        return Results.Ok(response);
    }

    private static async Task<IResult> GetPlaylistByIdAsync(
        Guid id,
        IPlaylistRepository repository,
        CancellationToken cancellationToken)
    {
        var playlist = await repository.GetByIdAsync(id, cancellationToken);
        
        if (playlist == null)
        {
            return Results.NotFound();
        }

        var response = MapToPlaylistResponse(playlist);
        return Results.Ok(response);
    }

    private static async Task<IResult> UpdatePlaylistStatusAsync(
        Guid id,
        UpdatePlaylistStatusRequest request,
        YtProducerDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var playlist = await dbContext.Playlists
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (playlist == null)
        {
            return Results.NotFound(new { message = $"Playlist {id} not found" });
        }

        if (string.IsNullOrWhiteSpace(request.Status) ||
            !Enum.TryParse<PlaylistStatus>(request.Status.Trim(), ignoreCase: true, out var newStatus))
        {
            return Results.BadRequest(new
            {
                message = $"Invalid status. Allowed: {string.Join(", ", Enum.GetNames<PlaylistStatus>())}"
            });
        }

        var previousStatus = playlist.Status;
        playlist.Status = newStatus;
        playlist.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.Ok(new UpdatePlaylistStatusResponse(
            playlist.Id,
            previousStatus.ToString(),
            playlist.Status.ToString()));
    }

    private static PlaylistResponse MapToPlaylistResponse(Playlist playlist)
    {
        return new PlaylistResponse(
            playlist.Id,
            playlist.Title,
            playlist.Theme,
            playlist.Description,
            playlist.PlaylistStrategy,
            playlist.Status.ToString(),
            playlist.TrackCount,
            playlist.YoutubePlaylistId,
            playlist.CreatedAtUtc,
            playlist.PublishedAtUtc,
            playlist.Tracks.Select(t => new TrackResponse(
                t.Id,
                t.PlaylistPosition,
                t.Title,
                t.YouTubeTitle,
                t.Style,
                t.Duration,
                t.TempoBpm,
                t.Key,
                t.EnergyLevel,
                t.Status.ToString(),
                t.SocialStat?.LikesCount ?? 0,
                t.SocialStat?.DislikesCount ?? 0
            )).ToList()
        );
    }

    private static async Task<IResult> AddTrackReactionAsync(
        Guid id,
        Guid trackId,
        TrackSocialReactionRequest request,
        YtProducerDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Reaction))
        {
            return Results.BadRequest(new { message = "Reaction is required." });
        }

        var normalizedReaction = request.Reaction.Trim().ToLowerInvariant();
        if (normalizedReaction is not ("like" or "dislike"))
        {
            return Results.BadRequest(new { message = "Reaction must be 'like' or 'dislike'." });
        }

        var track = await dbContext.Tracks
            .Include(x => x.SocialStat)
            .FirstOrDefaultAsync(
                x => x.Id == trackId && x.PlaylistId == id,
                cancellationToken);

        if (track == null)
        {
            return Results.NotFound(new { message = $"Track {trackId} was not found in playlist {id}." });
        }

        var socialStat = track.SocialStat;
        if (socialStat == null)
        {
            socialStat = new TrackSocialStat
            {
                Id = Guid.NewGuid(),
                TrackId = track.Id,
                PlaylistId = track.PlaylistId,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };

            dbContext.TrackSocialStats.Add(socialStat);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        var now = DateTimeOffset.UtcNow;
        if (normalizedReaction == "like")
        {
            await dbContext.TrackSocialStats
                .Where(x => x.TrackId == track.Id)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(x => x.LikesCount, x => x.LikesCount + 1)
                        .SetProperty(x => x.UpdatedAtUtc, now),
                    cancellationToken);
        }
        else
        {
            await dbContext.TrackSocialStats
                .Where(x => x.TrackId == track.Id)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(x => x.DislikesCount, x => x.DislikesCount + 1)
                        .SetProperty(x => x.UpdatedAtUtc, now),
                    cancellationToken);
        }

        track.UpdatedAtUtc = now;
        await dbContext.SaveChangesAsync(cancellationToken);

        socialStat = await dbContext.TrackSocialStats
            .AsNoTracking()
            .FirstAsync(x => x.TrackId == track.Id, cancellationToken);

        return Results.Ok(new TrackSocialStatResponse(
            track.Id,
            track.PlaylistId,
            socialStat.LikesCount,
            socialStat.DislikesCount));
    }

    private static async Task<IResult> GetPlaylistMediaAsync(
        Guid id,
        IPlaylistRepository repository,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var playlist = await repository.GetByIdAsync(id, cancellationToken);
        if (playlist == null)
        {
            return Results.NotFound();
        }

        var workingDirectory = Environment.GetEnvironmentVariable("YT_PRODUCER_PLAYLIST_WORKING_DIRECTORY")
            ?? configuration["YT_PRODUCER_PLAYLIST_WORKING_DIRECTORY"];
        if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            return Results.Ok(new PlaylistMediaResponse(id, Array.Empty<PlaylistTrackMediaResponse>()));
        }

        var playlistRoot = Path.Combine(workingDirectory, id.ToString());
        if (!Directory.Exists(playlistRoot))
        {
            return Results.Ok(new PlaylistMediaResponse(id, Array.Empty<PlaylistTrackMediaResponse>()));
        }

        var images = DiscoverMediaFiles(playlistRoot, searchSubfolder: null, new[] { ".jpg", ".jpeg", ".png", ".webp" });
        var videos = DiscoverMediaFiles(playlistRoot, searchSubfolder: null, new[] { ".mp4", ".mov", ".webm" });
        var audios = DiscoverMediaFiles(playlistRoot, searchSubfolder: null, new[] { ".mp3" });

        var grouped = new Dictionary<int, PlaylistTrackMediaResponse>();
        foreach (var entry in images)
        {
            if (!grouped.TryGetValue(entry.Position, out var existing))
            {
                existing = new PlaylistTrackMediaResponse(
                    entry.Position,
                    new List<PlaylistMediaFileResponse>(),
                    new List<PlaylistMediaFileResponse>(),
                    new List<PlaylistMediaFileResponse>());
            }

            ((List<PlaylistMediaFileResponse>)existing.Images).Add(entry.File);
            grouped[entry.Position] = existing;
        }

        foreach (var entry in videos)
        {
            if (!grouped.TryGetValue(entry.Position, out var existing))
            {
                existing = new PlaylistTrackMediaResponse(
                    entry.Position,
                    new List<PlaylistMediaFileResponse>(),
                    new List<PlaylistMediaFileResponse>(),
                    new List<PlaylistMediaFileResponse>());
            }

            ((List<PlaylistMediaFileResponse>)existing.Videos).Add(entry.File);
            grouped[entry.Position] = existing;
        }

        foreach (var entry in audios)
        {
            if (!grouped.TryGetValue(entry.Position, out var existing))
            {
                existing = new PlaylistTrackMediaResponse(
                    entry.Position,
                    new List<PlaylistMediaFileResponse>(),
                    new List<PlaylistMediaFileResponse>(),
                    new List<PlaylistMediaFileResponse>());
            }

            ((List<PlaylistMediaFileResponse>)existing.Audios).Add(entry.File);
            grouped[entry.Position] = existing;
        }

        var responseTracks = grouped.Values
            .OrderBy(t => t.PlaylistPosition)
            .Select(track => new PlaylistTrackMediaResponse(
                track.PlaylistPosition,
                track.Images.OrderBy(f => f.FileName, StringComparer.OrdinalIgnoreCase).ToList(),
                track.Videos.OrderBy(f => f.FileName, StringComparer.OrdinalIgnoreCase).ToList(),
                track.Audios.OrderBy(f => f.FileName, StringComparer.OrdinalIgnoreCase).ToList()))
            .ToList();

        return Results.Ok(new PlaylistMediaResponse(id, responseTracks));
    }

    private static async Task<IResult> GetPlaylistMusicPromptsAsync(
        Guid id,
        IPlaylistRepository repository,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var playlist = await repository.GetByIdAsync(id, cancellationToken);
        if (playlist == null)
        {
            return Results.NotFound();
        }

        var prompts = new Dictionary<int, string>();
        string? sourceFileName = null;

        var workingDirectory = Environment.GetEnvironmentVariable("YT_PRODUCER_PLAYLIST_WORKING_DIRECTORY")
            ?? configuration["YT_PRODUCER_PLAYLIST_WORKING_DIRECTORY"];

        if (!string.IsNullOrWhiteSpace(workingDirectory))
        {
            var playlistFolderPath = Path.Combine(workingDirectory, id.ToString());
            var promptFilePath = Path.Combine(playlistFolderPath, "missing_suno_prompts.txt");
            if (System.IO.File.Exists(promptFilePath))
            {
                foreach (var line in await System.IO.File.ReadAllLinesAsync(promptFilePath, cancellationToken))
                {
                    var match = Regex.Match(line, @"^(?<position>\d+):\s*(?<prompt>.+)$");
                    if (!match.Success)
                    {
                        continue;
                    }

                    if (int.TryParse(match.Groups["position"].Value, out var position))
                    {
                        var prompt = match.Groups["prompt"].Value.Trim();
                        if (!string.IsNullOrWhiteSpace(prompt))
                        {
                            prompts[position] = prompt;
                        }
                    }
                }

                if (prompts.Count > 0)
                {
                    sourceFileName = "missing_suno_prompts.txt";
                }
            }
        }

        if (prompts.Count == 0)
        {
            foreach (var track in playlist.Tracks)
            {
                var prompt = TryGetMetadataString(track.Metadata, "musicGenerationPrompt");
                if (!string.IsNullOrWhiteSpace(prompt))
                {
                    prompts[track.PlaylistPosition] = prompt.Trim();
                }
            }
        }

        var items = playlist.Tracks
            .OrderBy(track => track.PlaylistPosition)
            .Where(track => prompts.ContainsKey(track.PlaylistPosition))
            .Select(track => new PlaylistPromptItemResponse(
                track.PlaylistPosition,
                string.IsNullOrWhiteSpace(track.Title) ? $"Track {track.PlaylistPosition}" : track.Title.Trim(),
                prompts[track.PlaylistPosition]))
            .ToList();

        return Results.Ok(new PlaylistPromptResponse(id, "music", sourceFileName, items));
    }

    private static async Task<IResult> GetPlaylistImagePromptsAsync(
        Guid id,
        IPlaylistRepository repository,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var playlist = await repository.GetByIdAsync(id, cancellationToken);
        if (playlist == null)
        {
            return Results.NotFound();
        }

        var prompts = new Dictionary<int, string>();
        string? sourceFileName = null;

        var workingDirectory = Environment.GetEnvironmentVariable("YT_PRODUCER_PLAYLIST_WORKING_DIRECTORY")
            ?? configuration["YT_PRODUCER_PLAYLIST_WORKING_DIRECTORY"];

        if (!string.IsNullOrWhiteSpace(workingDirectory))
        {
            var playlistFolderPath = Path.Combine(workingDirectory, id.ToString());
            var promptFilePath = Path.Combine(playlistFolderPath, "missing_image_prompts.txt");
            if (System.IO.File.Exists(promptFilePath))
            {
                foreach (var line in await System.IO.File.ReadAllLinesAsync(promptFilePath, cancellationToken))
                {
                    var match = Regex.Match(line, @"^(?<position>\d+):\s*(?<prompt>.+)$");
                    if (!match.Success)
                    {
                        continue;
                    }

                    if (int.TryParse(match.Groups["position"].Value, out var position))
                    {
                        var prompt = match.Groups["prompt"].Value.Trim();
                        if (!string.IsNullOrWhiteSpace(prompt))
                        {
                            prompts[position] = prompt;
                        }
                    }
                }

                if (prompts.Count > 0)
                {
                    sourceFileName = "missing_image_prompts.txt";
                }
            }
        }

        if (prompts.Count == 0)
        {
            foreach (var track in playlist.Tracks)
            {
                var prompt = TryGetMetadataString(track.Metadata, "imagePrompt");
                if (!string.IsNullOrWhiteSpace(prompt))
                {
                    prompts[track.PlaylistPosition] = prompt.Trim();
                }
            }
        }

        var items = playlist.Tracks
            .OrderBy(track => track.PlaylistPosition)
            .Where(track => prompts.ContainsKey(track.PlaylistPosition))
            .Select(track => new PlaylistPromptItemResponse(
                track.PlaylistPosition,
                string.IsNullOrWhiteSpace(track.Title) ? $"Track {track.PlaylistPosition}" : track.Title.Trim(),
                prompts[track.PlaylistPosition]))
            .ToList();

        return Results.Ok(new PlaylistPromptResponse(id, "image", sourceFileName, items));
    }

    private static string? TryGetMetadataString(string? metadata, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(metadata))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(metadata);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (!document.RootElement.TryGetProperty(propertyName, out var property))
            {
                return null;
            }

            return property.ValueKind == JsonValueKind.String
                ? property.GetString()
                : property.ToString();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static async Task<IResult> SchedulePlaylistStartAsync(
        Guid id,
        YtProducerDbContext dbContext,
        IJobService jobService,
        CancellationToken cancellationToken)
    {
        var playlistExists = await dbContext.Playlists
            .AsNoTracking()
            .AnyAsync(x => x.Id == id, cancellationToken);

        if (!playlistExists)
        {
            return Results.NotFound(new { message = $"Playlist {id} not found" });
        }

        var payloadArguments = new CreatePlaylistInitJobArguments(id);
        var payloadJson = JsonSerializer.Serialize(new ScheduledCommandPayload(
            "playlist-init",
            1,
            JsonSerializer.SerializeToElement(payloadArguments)));

        var result = await jobService.CreateAsync(new Job
        {
            Type = JobType.PlaylistInit,
            TargetType = "playlist",
            TargetId = id,
            PayloadJson = payloadJson,
            MaxRetries = 3
        }, cancellationToken);

        return BuildScheduledPlaylistResponse(
            result,
            new SchedulePlaylistStartResponse(id, result.Job.Id, result.Job.Type.ToString(), result.Job.Status.ToString()));
    }

    private static async Task<IResult> SchedulePlaylistGenerateImagesAsync(
        Guid id,
        YtProducerDbContext dbContext,
        IJobService jobService,
        CancellationToken cancellationToken)
    {
        var playlistExists = await dbContext.Playlists
            .AsNoTracking()
            .AnyAsync(x => x.Id == id, cancellationToken);

        if (!playlistExists)
        {
            return Results.NotFound(new { message = $"Playlist {id} not found" });
        }

        var payloadArguments = new CreateGenerateAllImagesJobArguments(id);
        var payloadJson = JsonSerializer.Serialize(new ScheduledCommandPayload(
            "generate-all-images",
            1,
            JsonSerializer.SerializeToElement(payloadArguments)));

        var result = await jobService.CreateAsync(new Job
        {
            Type = JobType.GenerateAllImages,
            TargetType = "playlist",
            TargetId = id,
            PayloadJson = payloadJson,
            IdempotencyKey = Guid.NewGuid().ToString("N"),
            MaxRetries = 3
        }, cancellationToken);

        return BuildScheduledPlaylistResponse(
            result,
            new SchedulePlaylistGenerateImagesResponse(id, result.Job.Id, result.Job.Type.ToString(), result.Job.Status.ToString()));
    }

    private static async Task<IResult> SchedulePlaylistGenerateMusicAsync(
        Guid id,
        YtProducerDbContext dbContext,
        IJobService jobService,
        CancellationToken cancellationToken)
    {
        var playlistExists = await dbContext.Playlists
            .AsNoTracking()
            .AnyAsync(x => x.Id == id, cancellationToken);

        if (!playlistExists)
        {
            return Results.NotFound(new { message = $"Playlist {id} not found" });
        }

        var payloadArguments = new CreateGenerateAllMusicJobArguments(id);
        var payloadJson = JsonSerializer.Serialize(new ScheduledCommandPayload(
            "generate-all-music",
            1,
            JsonSerializer.SerializeToElement(payloadArguments)));

        var result = await jobService.CreateAsync(new Job
        {
            Type = JobType.GenerateAllMusic,
            TargetType = "playlist",
            TargetId = id,
            PayloadJson = payloadJson,
            IdempotencyKey = Guid.NewGuid().ToString("N"),
            MaxRetries = 3
        }, cancellationToken);

        return BuildScheduledPlaylistResponse(
            result,
            new SchedulePlaylistGenerateMusicResponse(id, result.Job.Id, result.Job.Type.ToString(), result.Job.Status.ToString()));
    }

    private static async Task<IResult> SchedulePlaylistGenerateThumbnailsAsync(
        Guid id,
        YtProducerDbContext dbContext,
        IJobService jobService,
        CancellationToken cancellationToken)
    {
        var playlistExists = await dbContext.Playlists
            .AsNoTracking()
            .AnyAsync(x => x.Id == id, cancellationToken);

        if (!playlistExists)
        {
            return Results.NotFound(new { message = $"Playlist {id} not found" });
        }

        var payloadArguments = new CreateGenerateThumbnailsJobArguments(id);
        var payloadJson = JsonSerializer.Serialize(new ScheduledCommandPayload(
            "track-create-youtube-video-thumbnail-v2",
            1,
            JsonSerializer.SerializeToElement(payloadArguments)));

        var result = await jobService.CreateAsync(new Job
        {
            Type = JobType.GenerateThumbnails,
            TargetType = "playlist",
            TargetId = id,
            PayloadJson = payloadJson,
            IdempotencyKey = Guid.NewGuid().ToString("N"),
            MaxRetries = 3
        }, cancellationToken);

        return BuildScheduledPlaylistResponse(
            result,
            new SchedulePlaylistGenerateThumbnailsResponse(id, result.Job.Id, result.Job.Type.ToString(), result.Job.Status.ToString()));
    }

    private static async Task<IResult> SchedulePlaylistGenerateVideosAsync(
        Guid id,
        SchedulePlaylistGenerateVideosRequest? request,
        YtProducerDbContext dbContext,
        IJobService jobService,
        CancellationToken cancellationToken)
    {
        var playlistExists = await dbContext.Playlists
            .AsNoTracking()
            .AnyAsync(x => x.Id == id, cancellationToken);

        if (!playlistExists)
        {
            return Results.NotFound(new { message = $"Playlist {id} not found" });
        }

        var profile = (request?.Profile ?? "fast").Trim().ToLowerInvariant();
        if (profile is not ("legacy" or "quality" or "fast"))
        {
            return Results.BadRequest(new { message = "Profile must be one of: legacy, quality, fast" });
        }

        var payloadArguments = new CreateGenerateVideosJobArguments(id, profile);
        var payloadJson = JsonSerializer.Serialize(new ScheduledCommandPayload(
            "generate-media-local",
            1,
            JsonSerializer.SerializeToElement(payloadArguments)));

        var result = await jobService.CreateAsync(new Job
        {
            Type = JobType.GenerateVideos,
            TargetType = "playlist",
            TargetId = id,
            PayloadJson = payloadJson,
            MaxRetries = 3
        }, cancellationToken);

        return BuildScheduledPlaylistResponse(
            result,
            new SchedulePlaylistGenerateVideosResponse(id, result.Job.Id, result.Job.Type.ToString(), result.Job.Status.ToString(), profile));
    }

    private static async Task<IResult> SchedulePlaylistGenerateYoutubePlaylistAsync(
        Guid id,
        YtProducerDbContext dbContext,
        IJobService jobService,
        CancellationToken cancellationToken)
    {
        var playlistExists = await dbContext.Playlists
            .AsNoTracking()
            .AnyAsync(x => x.Id == id, cancellationToken);

        if (!playlistExists)
        {
            return Results.NotFound(new { message = $"Playlist {id} not found" });
        }

        const string privacy = "unlisted";
        var payloadArguments = new CreateGenerateYoutubePlaylistJobArguments(id, privacy);
        var payloadJson = JsonSerializer.Serialize(new ScheduledCommandPayload(
            "generate-youtube-playlist",
            1,
            JsonSerializer.SerializeToElement(payloadArguments)));

        var result = await jobService.CreateAsync(new Job
        {
            Type = JobType.GenerateYoutubePlaylist,
            TargetType = "playlist",
            TargetId = id,
            PayloadJson = payloadJson,
            MaxRetries = 3
        }, cancellationToken);

        return BuildScheduledPlaylistResponse(
            result,
            new SchedulePlaylistGenerateYoutubePlaylistResponse(id, result.Job.Id, result.Job.Type.ToString(), result.Job.Status.ToString(), privacy));
    }

    private static async Task<IResult> SchedulePlaylistUploadYoutubeVideosAsync(
        Guid id,
        YtProducerDbContext dbContext,
        IJobService jobService,
        CancellationToken cancellationToken)
    {
        var playlistExists = await dbContext.Playlists
            .AsNoTracking()
            .AnyAsync(x => x.Id == id, cancellationToken);

        if (!playlistExists)
        {
            return Results.NotFound(new { message = $"Playlist {id} not found" });
        }

        var payloadArguments = new CreateUploadYoutubeVideosJobArguments(id);
        var payloadJson = JsonSerializer.Serialize(new ScheduledCommandPayload(
            "upload-youtube-videos",
            1,
            JsonSerializer.SerializeToElement(payloadArguments)));

        var result = await jobService.CreateAsync(new Job
        {
            Type = JobType.UploadYoutubeVideos,
            TargetType = "playlist",
            TargetId = id,
            PayloadJson = payloadJson,
            MaxRetries = 3
        }, cancellationToken);

        return BuildScheduledPlaylistResponse(
            result,
            new SchedulePlaylistUploadYoutubeVideosResponse(id, result.Job.Id, result.Job.Type.ToString(), result.Job.Status.ToString()));
    }

    private static async Task<IResult> SchedulePlaylistAddYoutubeVideosToPlaylistAsync(
        Guid id,
        YtProducerDbContext dbContext,
        IJobService jobService,
        CancellationToken cancellationToken)
    {
        var playlistExists = await dbContext.Playlists
            .AsNoTracking()
            .AnyAsync(x => x.Id == id, cancellationToken);

        if (!playlistExists)
        {
            return Results.NotFound(new { message = $"Playlist {id} not found" });
        }

        var payloadArguments = new CreateAddYoutubeVideosToPlaylistJobArguments(id);
        var payloadJson = JsonSerializer.Serialize(new ScheduledCommandPayload(
            "add-youtube-videos-to-playlist",
            1,
            JsonSerializer.SerializeToElement(payloadArguments)));

        var result = await jobService.CreateAsync(new Job
        {
            Type = JobType.AddYoutubeVideosToPlaylist,
            TargetType = "playlist",
            TargetId = id,
            PayloadJson = payloadJson,
            MaxRetries = 3
        }, cancellationToken);

        return BuildScheduledPlaylistResponse(
            result,
            new SchedulePlaylistAddYoutubeVideosToPlaylistResponse(id, result.Job.Id, result.Job.Type.ToString(), result.Job.Status.ToString()));
    }

    private static async Task<IResult> SchedulePlaylistGenerateYoutubeEngagementsAsync(
        Guid id,
        YtProducerDbContext dbContext,
        IJobService jobService,
        CancellationToken cancellationToken)
    {
        var playlistExists = await dbContext.Playlists
            .AsNoTracking()
            .AnyAsync(x => x.Id == id, cancellationToken);

        if (!playlistExists)
        {
            return Results.NotFound(new { message = $"Playlist {id} not found" });
        }

        var payloadArguments = new CreateGenerateYoutubeEngagementsJobArguments(id);
        var payloadJson = JsonSerializer.Serialize(new ScheduledCommandPayload(
            "generate-youtube-engagements",
            1,
            JsonSerializer.SerializeToElement(payloadArguments)));

        var result = await jobService.CreateAsync(new Job
        {
            Type = JobType.GenerateYoutubeEngagements,
            TargetType = "playlist",
            TargetId = id,
            PayloadJson = payloadJson,
            MaxRetries = 3
        }, cancellationToken);

        return BuildScheduledPlaylistResponse(
            result,
            new SchedulePlaylistGenerateYoutubeEngagementsResponse(id, result.Job.Id, result.Job.Type.ToString(), result.Job.Status.ToString()));
    }

    private static IResult BuildScheduledPlaylistResponse<TResponse>(JobCreateResult result, TResponse response)
    {
        return result.CreatedNew
            ? Results.Created($"/jobs/{result.Job.Id}", response)
            : Results.Ok(response);
    }

    private static IResult GetPlaylistMediaFileAsync(
        Guid id,
        string? relativePath,
        IConfiguration configuration)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return Results.NotFound();
        }

        var workingDirectory = Environment.GetEnvironmentVariable("YT_PRODUCER_PLAYLIST_WORKING_DIRECTORY")
            ?? configuration["YT_PRODUCER_PLAYLIST_WORKING_DIRECTORY"];
        if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            return Results.NotFound();
        }

        var playlistRoot = Path.Combine(workingDirectory, id.ToString());
        var fullRoot = Path.GetFullPath(playlistRoot);
        var candidatePath = Path.GetFullPath(Path.Combine(playlistRoot, relativePath));

        if (!candidatePath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
        {
            return Results.NotFound();
        }

        if (!System.IO.File.Exists(candidatePath))
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

    private static IResult SetPlaylistTrackBackgroundAsync(
        Guid id,
        SetPlaylistTrackBackgroundRequest request,
        IConfiguration configuration)
    {
        if (request.PlaylistPosition <= 0 || string.IsNullOrWhiteSpace(request.FileName))
        {
            return Results.BadRequest();
        }

        var fileName = Path.GetFileName(request.FileName);
        if (string.IsNullOrWhiteSpace(fileName) || !string.Equals(fileName, request.FileName, StringComparison.Ordinal))
        {
            return Results.BadRequest();
        }

        var workingDirectory = Environment.GetEnvironmentVariable("YT_PRODUCER_PLAYLIST_WORKING_DIRECTORY")
            ?? configuration["YT_PRODUCER_PLAYLIST_WORKING_DIRECTORY"];
        if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            return Results.NotFound();
        }

        var playlistRoot = Path.Combine(workingDirectory, id.ToString());
        if (!Directory.Exists(playlistRoot))
        {
            return Results.NotFound();
        }

        var sourcePath = Path.Combine(playlistRoot, fileName);
        if (!System.IO.File.Exists(sourcePath))
        {
            return Results.NotFound();
        }

        var sourceExtension = Path.GetExtension(fileName);
        if (string.IsNullOrWhiteSpace(sourceExtension))
        {
            return Results.BadRequest();
        }

        var allowedImageExtensions = new[] { ".jpg", ".jpeg", ".png", ".webp" };
        if (!allowedImageExtensions.Contains(sourceExtension, StringComparer.OrdinalIgnoreCase))
        {
            return Results.BadRequest();
        }

        var baseName = Path.GetFileNameWithoutExtension(fileName);
        var selectedMatch = Regex.Match(
            baseName,
            "^(?<pos>\\d+)(?:_(?<variant>\\d+))?$",
            RegexOptions.CultureInvariant);
        if (!selectedMatch.Success || !int.TryParse(selectedMatch.Groups["pos"].Value, out var selectedPosition))
        {
            return Results.BadRequest();
        }

        if (selectedPosition != request.PlaylistPosition)
        {
            return Results.BadRequest();
        }

        if (!selectedMatch.Groups["variant"].Success)
        {
            return Results.Ok(new SetPlaylistTrackBackgroundResponse(
                id,
                request.PlaylistPosition,
                fileName,
                false));
        }

        var masterName = $"{request.PlaylistPosition}{sourceExtension.ToLowerInvariant()}";
        var masterPath = Path.Combine(playlistRoot, masterName);
        var sourceFullPath = Path.GetFullPath(sourcePath);
        var masterFullPath = Path.GetFullPath(masterPath);
        var rootFullPath = Path.GetFullPath(playlistRoot);
        if (!sourceFullPath.StartsWith(rootFullPath, StringComparison.OrdinalIgnoreCase)
            || !masterFullPath.StartsWith(rootFullPath, StringComparison.OrdinalIgnoreCase))
        {
            return Results.BadRequest();
        }

        var tempName = $"{request.PlaylistPosition}_swap_{Guid.NewGuid():N}{sourceExtension.ToLowerInvariant()}";
        var tempPath = Path.Combine(playlistRoot, tempName);

        try
        {
            System.IO.File.Move(sourcePath, tempPath);
            if (System.IO.File.Exists(masterPath))
            {
                System.IO.File.Move(masterPath, sourcePath);
            }
            System.IO.File.Move(tempPath, masterPath);
        }
        catch
        {
            try
            {
                if (System.IO.File.Exists(tempPath) && !System.IO.File.Exists(sourcePath))
                {
                    System.IO.File.Move(tempPath, sourcePath);
                }
            }
            catch
            {
                // Best effort rollback.
            }

            return Results.BadRequest();
        }

        return Results.Ok(new SetPlaylistTrackBackgroundResponse(
            id,
            request.PlaylistPosition,
            masterName,
            true));
    }

    private static IResult MovePlaylistTrackImageAsync(
        Guid id,
        MovePlaylistTrackImageRequest request,
        IConfiguration configuration)
    {
        if (request.PlaylistPosition <= 0 || string.IsNullOrWhiteSpace(request.FileName))
        {
            return Results.BadRequest();
        }

        var fileName = Path.GetFileName(request.FileName);
        if (string.IsNullOrWhiteSpace(fileName) || !string.Equals(fileName, request.FileName, StringComparison.Ordinal))
        {
            return Results.BadRequest();
        }

        var workingDirectory = Environment.GetEnvironmentVariable("YT_PRODUCER_PLAYLIST_WORKING_DIRECTORY")
            ?? configuration["YT_PRODUCER_PLAYLIST_WORKING_DIRECTORY"];
        if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            return Results.NotFound();
        }

        var playlistRoot = Path.Combine(workingDirectory, id.ToString());
        if (!Directory.Exists(playlistRoot))
        {
            return Results.NotFound();
        }

        var sourcePath = Path.Combine(playlistRoot, fileName);
        if (!System.IO.File.Exists(sourcePath))
        {
            return Results.NotFound();
        }

        var sourceExtension = Path.GetExtension(fileName);
        if (string.IsNullOrWhiteSpace(sourceExtension))
        {
            return Results.BadRequest();
        }

        var allowedImageExtensions = new[] { ".jpg", ".jpeg", ".png", ".webp" };
        if (!allowedImageExtensions.Contains(sourceExtension, StringComparer.OrdinalIgnoreCase))
        {
            return Results.BadRequest();
        }

        var baseName = Path.GetFileNameWithoutExtension(fileName);
        var selectedMatch = Regex.Match(
            baseName,
            "^(?<pos>\\d+)(?:_(?<variant>\\d+))?$",
            RegexOptions.CultureInvariant);
        if (!selectedMatch.Success || !int.TryParse(selectedMatch.Groups["pos"].Value, out var selectedPosition))
        {
            return Results.BadRequest();
        }

        if (selectedPosition != request.PlaylistPosition)
        {
            return Results.BadRequest();
        }

        var fullPlaylistRoot = Path.GetFullPath(playlistRoot);
        var fullSourcePath = Path.GetFullPath(sourcePath);
        if (!fullSourcePath.StartsWith(fullPlaylistRoot, StringComparison.OrdinalIgnoreCase))
        {
            return Results.BadRequest();
        }

        var tmpPlaylistPath = Path.Combine(workingDirectory, "tmp-playlist");
        Directory.CreateDirectory(tmpPlaylistPath);

        var existingNumbers = Directory.EnumerateFiles(tmpPlaylistPath)
            .Select(Path.GetFileNameWithoutExtension)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => int.TryParse(name, out var parsed) ? parsed : 0)
            .Where(value => value > 0)
            .ToList();
        var nextNumber = existingNumbers.Count == 0 ? 1 : existingNumbers.Max() + 1;
        var targetFileName = $"{nextNumber}{sourceExtension.ToLowerInvariant()}";
        var targetPath = Path.Combine(tmpPlaylistPath, targetFileName);

        try
        {
            System.IO.File.Move(sourcePath, targetPath, false);
        }
        catch
        {
            return Results.BadRequest();
        }

        return Results.Ok(new MovePlaylistTrackImageResponse(
            id,
            request.PlaylistPosition,
            fileName,
            targetFileName,
            Path.Combine("tmp-playlist", targetFileName).Replace("\\", "/")));
    }

    private static IResult DeletePlaylistTrackThumbnailAsync(
        Guid id,
        DeletePlaylistTrackThumbnailRequest request,
        IConfiguration configuration)
    {
        if (request.PlaylistPosition <= 0 || string.IsNullOrWhiteSpace(request.FileName))
        {
            return Results.BadRequest();
        }

        var fileName = Path.GetFileName(request.FileName);
        if (string.IsNullOrWhiteSpace(fileName) || !string.Equals(fileName, request.FileName, StringComparison.Ordinal))
        {
            return Results.BadRequest();
        }

        var workingDirectory = Environment.GetEnvironmentVariable("YT_PRODUCER_PLAYLIST_WORKING_DIRECTORY")
            ?? configuration["YT_PRODUCER_PLAYLIST_WORKING_DIRECTORY"];
        if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            return Results.NotFound();
        }

        var playlistRoot = Path.Combine(workingDirectory, id.ToString());
        if (!Directory.Exists(playlistRoot))
        {
            return Results.NotFound();
        }

        var sourcePath = Path.Combine(playlistRoot, fileName);
        if (!System.IO.File.Exists(sourcePath))
        {
            return Results.NotFound();
        }

        var sourceExtension = Path.GetExtension(fileName);
        var allowedImageExtensions = new[] { ".jpg", ".jpeg", ".png", ".webp" };
        if (string.IsNullOrWhiteSpace(sourceExtension)
            || !allowedImageExtensions.Contains(sourceExtension, StringComparer.OrdinalIgnoreCase))
        {
            return Results.BadRequest();
        }

        var baseName = Path.GetFileNameWithoutExtension(fileName);
        var match = Regex.Match(
            baseName,
            "^(?<pos>\\d+)_thumbnail(?:_(?<variant>\\d+))?$",
            RegexOptions.CultureInvariant);
        if (!match.Success || !int.TryParse(match.Groups["pos"].Value, out var parsedPosition))
        {
            return Results.BadRequest();
        }

        if (parsedPosition != request.PlaylistPosition)
        {
            return Results.BadRequest();
        }

        var fullPlaylistRoot = Path.GetFullPath(playlistRoot);
        var fullSourcePath = Path.GetFullPath(sourcePath);
        if (!fullSourcePath.StartsWith(fullPlaylistRoot, StringComparison.OrdinalIgnoreCase))
        {
            return Results.BadRequest();
        }

        try
        {
            System.IO.File.Delete(sourcePath);
        }
        catch
        {
            return Results.BadRequest();
        }

        return Results.Ok(new DeletePlaylistTrackThumbnailResponse(
            id,
            request.PlaylistPosition,
            fileName,
            true));
    }

    private static IResult MovePlaylistTrackAudioAsync(
        Guid id,
        MovePlaylistTrackAudioRequest request,
        IConfiguration configuration)
    {
        if (request.PlaylistPosition <= 0 || string.IsNullOrWhiteSpace(request.FileName))
        {
            return Results.BadRequest();
        }

        var fileName = Path.GetFileName(request.FileName);
        if (string.IsNullOrWhiteSpace(fileName) || !string.Equals(fileName, request.FileName, StringComparison.Ordinal))
        {
            return Results.BadRequest();
        }

        var workingDirectory = Environment.GetEnvironmentVariable("YT_PRODUCER_PLAYLIST_WORKING_DIRECTORY")
            ?? configuration["YT_PRODUCER_PLAYLIST_WORKING_DIRECTORY"];
        if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            return Results.NotFound();
        }

        var playlistRoot = Path.Combine(workingDirectory, id.ToString());
        if (!Directory.Exists(playlistRoot))
        {
            return Results.NotFound();
        }

        var sourcePath = Path.Combine(playlistRoot, fileName);
        if (!System.IO.File.Exists(sourcePath))
        {
            return Results.NotFound();
        }

        var sourceExtension = Path.GetExtension(fileName);
        if (!string.Equals(sourceExtension, ".mp3", StringComparison.OrdinalIgnoreCase))
        {
            return Results.BadRequest();
        }

        var baseName = Path.GetFileNameWithoutExtension(fileName);
        var selectedMatch = Regex.Match(
            baseName,
            "^(?<pos>\\d+)(?:_(?<variant>\\d+))?$",
            RegexOptions.CultureInvariant);
        if (!selectedMatch.Success || !int.TryParse(selectedMatch.Groups["pos"].Value, out var selectedPosition))
        {
            return Results.BadRequest();
        }

        if (selectedPosition != request.PlaylistPosition)
        {
            return Results.BadRequest();
        }

        var fullPlaylistRoot = Path.GetFullPath(playlistRoot);
        var fullSourcePath = Path.GetFullPath(sourcePath);
        if (!fullSourcePath.StartsWith(fullPlaylistRoot, StringComparison.OrdinalIgnoreCase))
        {
            return Results.BadRequest();
        }

        var tmpPlaylistPath = Path.Combine(workingDirectory, "tmp-playlist");
        Directory.CreateDirectory(tmpPlaylistPath);

        var existingNumbers = Directory.EnumerateFiles(tmpPlaylistPath)
            .Select(Path.GetFileNameWithoutExtension)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => int.TryParse(name, out var parsed) ? parsed : 0)
            .Where(value => value > 0)
            .ToList();
        var nextNumber = existingNumbers.Count == 0 ? 1 : existingNumbers.Max() + 1;

        var targetAudioFileName = $"{nextNumber}.mp3";
        var targetAudioPath = Path.Combine(tmpPlaylistPath, targetAudioFileName);
        var sourceJsonPath = Path.ChangeExtension(sourcePath, ".json");
        var targetJsonFileName = $"{nextNumber}.json";
        var targetJsonPath = Path.Combine(tmpPlaylistPath, targetJsonFileName);

        try
        {
            System.IO.File.Move(sourcePath, targetAudioPath, false);
            if (System.IO.File.Exists(sourceJsonPath))
            {
                System.IO.File.Move(sourceJsonPath, targetJsonPath, true);
            }
        }
        catch
        {
            return Results.BadRequest();
        }

        return Results.Ok(new MovePlaylistTrackAudioResponse(
            id,
            request.PlaylistPosition,
            fileName,
            targetAudioFileName,
            System.IO.File.Exists(targetJsonPath) ? targetJsonFileName : null,
            Path.Combine("tmp-playlist", targetAudioFileName).Replace("\\", "/")));
    }

    private static IResult DeletePlaylistTrackAudioAsync(
        Guid id,
        DeletePlaylistTrackAudioRequest request,
        IConfiguration configuration)
    {
        if (request.PlaylistPosition <= 0 || string.IsNullOrWhiteSpace(request.FileName))
        {
            return Results.BadRequest();
        }

        var fileName = Path.GetFileName(request.FileName);
        if (string.IsNullOrWhiteSpace(fileName) || !string.Equals(fileName, request.FileName, StringComparison.Ordinal))
        {
            return Results.BadRequest();
        }

        var workingDirectory = Environment.GetEnvironmentVariable("YT_PRODUCER_PLAYLIST_WORKING_DIRECTORY")
            ?? configuration["YT_PRODUCER_PLAYLIST_WORKING_DIRECTORY"];
        if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            return Results.NotFound();
        }

        var playlistRoot = Path.Combine(workingDirectory, id.ToString());
        if (!Directory.Exists(playlistRoot))
        {
            return Results.NotFound();
        }

        var sourcePath = Path.Combine(playlistRoot, fileName);
        if (!System.IO.File.Exists(sourcePath))
        {
            return Results.NotFound();
        }

        var sourceExtension = Path.GetExtension(fileName);
        if (!string.Equals(sourceExtension, ".mp3", StringComparison.OrdinalIgnoreCase))
        {
            return Results.BadRequest();
        }

        var baseName = Path.GetFileNameWithoutExtension(fileName);
        var selectedMatch = Regex.Match(
            baseName,
            "^(?<pos>\\d+)(?:_(?<variant>\\d+))?$",
            RegexOptions.CultureInvariant);
        if (!selectedMatch.Success || !int.TryParse(selectedMatch.Groups["pos"].Value, out var selectedPosition))
        {
            return Results.BadRequest();
        }

        if (selectedPosition != request.PlaylistPosition)
        {
            return Results.BadRequest();
        }

        var fullPlaylistRoot = Path.GetFullPath(playlistRoot);
        var fullSourcePath = Path.GetFullPath(sourcePath);
        if (!fullSourcePath.StartsWith(fullPlaylistRoot, StringComparison.OrdinalIgnoreCase))
        {
            return Results.BadRequest();
        }

        var sourceJsonPath = Path.ChangeExtension(sourcePath, ".json");
        try
        {
            System.IO.File.Delete(sourcePath);
            if (System.IO.File.Exists(sourceJsonPath))
            {
                System.IO.File.Delete(sourceJsonPath);
            }
        }
        catch
        {
            return Results.BadRequest();
        }

        var wasMaster = !selectedMatch.Groups["variant"].Success;
        var promoted = false;
        string? promotedFileName = null;
        if (wasMaster)
        {
            var promotionCandidates = Directory.EnumerateFiles(playlistRoot, $"{request.PlaylistPosition}_*.mp3")
                .Where(path =>
                {
                    var name = Path.GetFileName(path);
                    if (string.IsNullOrWhiteSpace(name) || name.StartsWith("._", StringComparison.Ordinal))
                    {
                        return false;
                    }

                    var nameWithoutExt = Path.GetFileNameWithoutExtension(name);
                    return Regex.IsMatch(nameWithoutExt, $"^{request.PlaylistPosition}_\\d+$", RegexOptions.CultureInvariant);
                })
                .OrderBy(path =>
                {
                    var candidateName = Path.GetFileNameWithoutExtension(path);
                    var match = Regex.Match(candidateName, "^(?<pos>\\d+)_(?<variant>\\d+)$", RegexOptions.CultureInvariant);
                    return match.Success && int.TryParse(match.Groups["variant"].Value, out var variant)
                        ? variant
                        : int.MaxValue;
                })
                .ToList();

            var promotePath = promotionCandidates.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(promotePath))
            {
                var masterAudioPath = Path.Combine(playlistRoot, $"{request.PlaylistPosition}.mp3");
                var promoteJsonPath = Path.ChangeExtension(promotePath, ".json");
                var masterJsonPath = Path.Combine(playlistRoot, $"{request.PlaylistPosition}.json");

                try
                {
                    System.IO.File.Move(promotePath, masterAudioPath, true);
                    if (System.IO.File.Exists(promoteJsonPath))
                    {
                        System.IO.File.Move(promoteJsonPath, masterJsonPath, true);
                    }

                    promoted = true;
                    promotedFileName = $"{request.PlaylistPosition}.mp3";
                }
                catch
                {
                    return Results.BadRequest();
                }
            }
        }

        return Results.Ok(new DeletePlaylistTrackAudioResponse(
            id,
            request.PlaylistPosition,
            fileName,
            true,
            promoted,
            promotedFileName));
    }

    private static IReadOnlyList<(int Position, PlaylistMediaFileResponse File)> DiscoverMediaFiles(
        string playlistRoot,
        string? searchSubfolder,
        IReadOnlyCollection<string> extensions)
    {
        var baseDir = string.IsNullOrWhiteSpace(searchSubfolder)
            ? playlistRoot
            : Path.Combine(playlistRoot, searchSubfolder);

        if (!Directory.Exists(baseDir))
        {
            return Array.Empty<(int Position, PlaylistMediaFileResponse File)>();
        }

        var files = Directory.EnumerateFiles(baseDir)
            .Where(path =>
            {
                var name = Path.GetFileName(path);
                if (string.IsNullOrWhiteSpace(name) || name.StartsWith("._", StringComparison.Ordinal))
                {
                    return false;
                }

                var extension = Path.GetExtension(name);
                return extensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
            })
            .ToList();

        var results = new List<(int Position, PlaylistMediaFileResponse File)>();
        foreach (var filePath in files)
        {
            var name = Path.GetFileName(filePath);
            var baseName = Path.GetFileNameWithoutExtension(name);
            var position = ParsePlaylistPosition(baseName);
            if (position == null)
            {
                continue;
            }

            var relativePath = string.IsNullOrWhiteSpace(searchSubfolder)
                ? name
                : $"{searchSubfolder}/{name}";
            var url = $"/playlists/{Path.GetFileName(playlistRoot)}/media/{relativePath}";
            results.Add((position.Value, new PlaylistMediaFileResponse(name, url)));
        }

        return results;
    }

    private static int? ParsePlaylistPosition(string baseName)
    {
        var thumbnailMatch = Regex.Match(
            baseName,
            "^(?<pos>\\d+)_thumbnail(?:_\\d+)?$",
            RegexOptions.CultureInvariant);
        if (thumbnailMatch.Success && int.TryParse(thumbnailMatch.Groups["pos"].Value, out var thumbnailPosition))
        {
            return thumbnailPosition;
        }

        var match = Regex.Match(
            baseName,
            "^(?<pos>\\d+)(?:_\\d+)?$",
            RegexOptions.CultureInvariant);
        if (!match.Success)
        {
            return null;
        }

        if (int.TryParse(match.Groups["pos"].Value, out var position))
        {
            return position;
        }

        return null;
    }

    private static async Task<IResult> GetPlaylistYoutubeVideosAsync(
        Guid id,
        YtProducerDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var items = await dbContext.TrackOnYoutube
            .AsNoTracking()
            .Where(x => x.PlaylistId == id)
            .OrderBy(x => x.PlaylistPosition)
            .ThenByDescending(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        var response = items.Select(item => new TrackOnYoutubeResponse(
            item.Id,
            item.TrackId,
            item.PlaylistId,
            item.PlaylistPosition,
            item.VideoId,
            item.Url,
            item.Title,
            item.Description,
            item.Privacy,
            item.FilePath,
            item.Status,
            item.Metadata,
            item.CreatedAtUtc)).ToList();

        return Results.Ok(response);
    }

    private static async Task<IResult> GetPlaylistTrackVideoGenerationsAsync(
        Guid id,
        YtProducerDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var items = await dbContext.TrackVideoGenerations
            .AsNoTracking()
            .Where(x => x.PlaylistId == id)
            .OrderBy(x => x.PlaylistPosition)
            .ThenBy(x => x.UpdatedAtUtc)
            .ToListAsync(cancellationToken);

        var response = items
            .Select(MapToTrackVideoGenerationResponse)
            .ToList();

        return Results.Ok(response);
    }

    private static async Task<IResult> GetPlaylistTrackVideoGenerationByPositionAsync(
        Guid id,
        int position,
        YtProducerDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (position <= 0)
        {
            return Results.NotFound();
        }

        var item = await dbContext.TrackVideoGenerations
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.PlaylistId == id && x.PlaylistPosition == position, cancellationToken);

        if (item == null)
        {
            return Results.NotFound();
        }

        return Results.Ok(MapToTrackVideoGenerationResponse(item));
    }

    private static async Task<IResult> UpsertPlaylistTrackVideoGenerationAsync(
        Guid id,
        int position,
        UpsertTrackVideoGenerationRequest request,
        YtProducerDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (position <= 0)
        {
            return Results.NotFound();
        }

        var track = await dbContext.Tracks
            .FirstOrDefaultAsync(t => t.PlaylistId == id && t.PlaylistPosition == position, cancellationToken);

        if (track == null)
        {
            return Results.NotFound();
        }

        var item = await dbContext.TrackVideoGenerations
            .FirstOrDefaultAsync(x => x.TrackId == track.Id, cancellationToken);

        if (item == null)
        {
            item = new TrackVideoGeneration
            {
                Id = Guid.NewGuid(),
                TrackId = track.Id,
                PlaylistId = track.PlaylistId,
                PlaylistPosition = track.PlaylistPosition,
                Status = "Pending",
                ProgressPercent = 0,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };
            dbContext.TrackVideoGenerations.Add(item);
        }

        item.Status = string.IsNullOrWhiteSpace(request.Status) ? item.Status : request.Status.Trim();
        item.ProgressPercent = Math.Clamp(request.ProgressPercent ?? item.ProgressPercent, 0, 100);
        item.ProgressCurrentFrame = request.ProgressCurrentFrame ?? item.ProgressCurrentFrame;
        item.ProgressTotalFrames = request.ProgressTotalFrames ?? item.ProgressTotalFrames;
        item.TrackDurationSeconds = request.TrackDurationSeconds ?? item.TrackDurationSeconds;
        item.ImagePath = request.ImagePath ?? item.ImagePath;
        item.AudioPath = request.AudioPath ?? item.AudioPath;
        item.TempDir = request.TempDir ?? item.TempDir;
        item.OutputDir = request.OutputDir ?? item.OutputDir;
        item.Width = request.Width ?? item.Width;
        item.Height = request.Height ?? item.Height;
        item.Fps = request.Fps ?? item.Fps;
        item.EqBands = request.EqBands ?? item.EqBands;
        item.VideoBitrate = request.VideoBitrate ?? item.VideoBitrate;
        item.AudioBitrate = request.AudioBitrate ?? item.AudioBitrate;
        item.Seed = request.Seed ?? item.Seed;
        item.UseGpu = request.UseGpu ?? item.UseGpu;
        item.KeepTemp = request.KeepTemp ?? item.KeepTemp;
        item.UseRawPipe = request.UseRawPipe ?? item.UseRawPipe;
        item.RendererVariant = request.RendererVariant ?? item.RendererVariant;
        item.OutputFileNameOverride = request.OutputFileNameOverride ?? item.OutputFileNameOverride;
        item.LogoPath = request.LogoPath ?? item.LogoPath;
        item.OutputVideoPath = request.OutputVideoPath ?? item.OutputVideoPath;
        item.AnalysisPath = request.AnalysisPath ?? item.AnalysisPath;
        item.FfmpegCommand = request.FfmpegCommand ?? item.FfmpegCommand;
        item.ErrorMessage = request.ErrorMessage ?? item.ErrorMessage;
        item.Metadata = request.Metadata ?? item.Metadata;
        item.StartedAtUtc = request.StartedAtUtc ?? item.StartedAtUtc;
        item.FinishedAtUtc = request.FinishedAtUtc ?? item.FinishedAtUtc;
        item.UpdatedAtUtc = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.Ok(MapToTrackVideoGenerationResponse(item));
    }

    private static async Task<IResult> ScheduleTrackLoopAsync(
        Guid id,
        CreateTrackLoopRequest request,
        YtProducerDbContext dbContext,
        IJobService jobService,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        if (request.LoopCount < 2)
        {
            return Results.BadRequest(new { message = "loopCount must be 2 or greater" });
        }

        var playlist = await dbContext.Playlists
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (playlist is null)
        {
            return Results.NotFound(new { message = $"Playlist {id} not found" });
        }

        if (string.IsNullOrWhiteSpace(playlist.YoutubePlaylistId))
        {
            return Results.BadRequest(new { message = "Playlist must have youtube playlist created before scheduling loops" });
        }

        var track = await dbContext.Tracks
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == request.TrackId && x.PlaylistId == id, cancellationToken);

        if (track is null)
        {
            return Results.NotFound(new { message = $"Track {request.TrackId} not found in playlist {id}" });
        }

        var workingDirectory = Environment.GetEnvironmentVariable("YT_PRODUCER_PLAYLIST_WORKING_DIRECTORY")
            ?? configuration["YT_PRODUCER_PLAYLIST_WORKING_DIRECTORY"];
        var playlistRoot = string.IsNullOrWhiteSpace(workingDirectory)
            ? null
            : Path.Combine(workingDirectory, id.ToString());

        var now = DateTimeOffset.UtcNow;
        var loop = new TrackLoop
        {
            Id = Guid.NewGuid(),
            PlaylistId = id,
            TrackId = track.Id,
            TrackPosition = track.PlaylistPosition,
            LoopCount = request.LoopCount,
            Status = TrackLoopStatus.Pending,
            SourceAudioPath = ResolvePreferredMediaFile(playlistRoot, track.PlaylistPosition, new[] { ".mp3" }),
            SourceImagePath = ResolvePreferredMediaFile(playlistRoot, track.PlaylistPosition, new[] { ".jpg", ".jpeg", ".png", ".webp" }),
            SourceVideoPath = ResolvePreferredMediaFile(playlistRoot, track.PlaylistPosition, new[] { ".mp4", ".mov", ".webm" }),
            ThumbnailPath = ResolvePreferredMediaFile(playlistRoot, track.PlaylistPosition, new[] { ".jpg", ".jpeg", ".png", ".webp" }, "_thumbnail"),
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

        var result = await jobService.CreateAsync(new Job
        {
            Type = JobType.CreateTrackLoop,
            TargetType = "track_loop",
            TargetId = loop.Id,
            PayloadJson = payloadJson,
            MaxRetries = 3
        }, cancellationToken);

        return Results.Created(
            $"/playlists/{id}/track-loops/{loop.Id}",
            new ScheduleTrackLoopResponse(result.Job.Id, result.Job.Type.ToString(), MapToTrackLoopResponse(loop)));
    }

    private static TrackVideoGenerationResponse MapToTrackVideoGenerationResponse(TrackVideoGeneration item)
    {
        return new TrackVideoGenerationResponse(
            item.Id,
            item.TrackId,
            item.PlaylistId,
            item.PlaylistPosition,
            item.Status,
            item.ProgressPercent,
            item.ProgressCurrentFrame,
            item.ProgressTotalFrames,
            item.TrackDurationSeconds,
            item.ImagePath,
            item.AudioPath,
            item.TempDir,
            item.OutputDir,
            item.Width,
            item.Height,
            item.Fps,
            item.EqBands,
            item.VideoBitrate,
            item.AudioBitrate,
            item.Seed,
            item.UseGpu,
            item.KeepTemp,
            item.UseRawPipe,
            item.RendererVariant,
            item.OutputFileNameOverride,
            item.LogoPath,
            item.OutputVideoPath,
            item.AnalysisPath,
            item.FfmpegCommand,
            item.ErrorMessage,
            item.Metadata,
            item.StartedAtUtc,
            item.FinishedAtUtc,
            item.CreatedAtUtc,
            item.UpdatedAtUtc);
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
            : Regex.Escape(requiredNameSuffix);
        var match = Regex.Match(
            fileNameWithoutExtension,
            $"^{Regex.Escape(positionPrefix)}{escapedSuffix}_(\\d+)$",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        if (!match.Success)
        {
            return null;
        }

        return int.TryParse(match.Groups[1].Value, out var parsed) ? parsed : null;
    }
}
