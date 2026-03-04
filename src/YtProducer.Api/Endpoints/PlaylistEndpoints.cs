using YtProducer.Contracts.Playlists;
using YtProducer.Contracts.Tracks;
using YtProducer.Domain.Entities;
using YtProducer.Domain.Enums;
using YtProducer.Infrastructure.Services;

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
}
