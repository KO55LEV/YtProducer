using YtProducer.Contracts.Playlists;
using YtProducer.Contracts.Tracks;
using YtProducer.Contracts.Tracks;
using YtProducer.Domain.Entities;
using YtProducer.Domain.Enums;
using YtProducer.Infrastructure.Services;
using YtProducer.Infrastructure.Persistence;
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
        var match = Regex.Match(baseName, "^(?<pos>\\d+)(?:_\\d+)?$", RegexOptions.CultureInvariant);
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
