using YtProducer.Contracts.Playlists;
using YtProducer.Contracts.Tracks;
using YtProducer.Domain.Entities;
using YtProducer.Domain.Enums;
using YtProducer.Infrastructure.Services;
using YtProducer.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.StaticFiles;
using System.Text.RegularExpressions;

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

        group.MapGet("/{id:guid}/media", GetPlaylistMediaAsync)
            .WithName("GetPlaylistMedia")
            .Produces<PlaylistMediaResponse>(StatusCodes.Status200OK);

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

        return app;
    }

    private static async Task<IResult> CreatePlaylistAsync(
        CreatePlaylistRequest request,
        IPlaylistRepository repository,
        CancellationToken cancellationToken)
    {
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
                t.Status.ToString()
            )).ToList()
        );
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
}
