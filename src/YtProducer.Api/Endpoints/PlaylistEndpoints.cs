using YtProducer.Contracts.Playlists;
using YtProducer.Contracts.Tracks;

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

    private static async Task<IResult> CreatePlaylistAsync(CreatePlaylistRequest request, CancellationToken cancellationToken)
    {
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();

        var response = new PlaylistResponse(
            Guid.NewGuid(),
            request.Title,
            request.Description,
            "Draft",
            Array.Empty<TrackResponse>());

        return Results.Created($"/playlists/{response.Id}", response);
    }

    private static async Task<IResult> GetPlaylistsAsync(CancellationToken cancellationToken)
    {
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();

        var playlists = new List<PlaylistResponse>
        {
            new(
                Guid.Parse("11111111-1111-1111-1111-111111111111"),
                "Daily Mix",
                "Placeholder playlist for orchestration dashboard.",
                "Active",
                new List<TrackResponse>
                {
                    new(
                        Guid.Parse("22222222-2222-2222-2222-222222222222"),
                        "Ambient Intro",
                        "Ready",
                        1)
                })
        };

        return Results.Ok(playlists);
    }

    private static async Task<IResult> GetPlaylistByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();

        if (id == Guid.Empty)
        {
            return Results.NotFound();
        }

        var response = new PlaylistResponse(
            id,
            "Requested Playlist",
            "Placeholder detail response.",
            "Draft",
            new List<TrackResponse>
            {
                new(
                    Guid.Parse("33333333-3333-3333-3333-333333333333"),
                    "Lead Track",
                    "Pending",
                    1)
            });

        return Results.Ok(response);
    }
}
