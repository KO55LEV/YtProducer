using Microsoft.AspNetCore.Mvc;
using YtProducer.Contracts.YoutubePlaylists;
using YtProducer.Domain.Entities;
using YtProducer.Infrastructure.Services;

namespace YtProducer.Api.Endpoints;

public static class YoutubePlaylistEndpoints
{
    public static IEndpointRouteBuilder MapYoutubePlaylistEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/youtube-playlists").WithTags("YouTubePlaylists");

        group.MapPost(string.Empty, CreateAsync)
            .WithName("CreateYoutubePlaylist")
            .Produces<YoutubePlaylistResponse>(StatusCodes.Status201Created);

        group.MapGet(string.Empty, GetAllAsync)
            .WithName("GetYoutubePlaylists")
            .Produces<IReadOnlyList<YoutubePlaylistResponse>>(StatusCodes.Status200OK);

        group.MapGet("/{id:guid}", GetByIdAsync)
            .WithName("GetYoutubePlaylistById")
            .Produces<YoutubePlaylistResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);


        group.MapPut("/{id:guid}", UpdateAsync)
            .WithName("UpdateYoutubePlaylist")
            .Produces<YoutubePlaylistResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        group.MapDelete("/{id:guid}", DeleteAsync)
            .WithName("DeleteYoutubePlaylist")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> GetAllAsync(
        [FromServices] IYoutubePlaylistRepository repository,
        CancellationToken cancellationToken)
    {
        var playlists = await repository.GetAllAsync(cancellationToken);
        var response = playlists.Select(MapToResponse).ToList();
        return Results.Ok(response);
    }

    private static async Task<IResult> GetByIdAsync(
        [FromRoute] Guid id,
        [FromServices] IYoutubePlaylistRepository repository,
        CancellationToken cancellationToken)
    {
        var playlist = await repository.GetByIdAsync(id, cancellationToken);
        if (playlist == null)
        {
            return Results.NotFound();
        }

        return Results.Ok(MapToResponse(playlist));
    }

    private static async Task<IResult> CreateAsync(
        [FromBody] CreateYoutubePlaylistRequest request,
        [FromServices] IYoutubePlaylistRepository repository,
        CancellationToken cancellationToken)
    {
        var playlist = new YoutubePlaylist
        {
            YoutubePlaylistId = request.YoutubePlaylistId,
            Title = request.Title,
            Description = request.Description,
            Status = request.Status,
            PrivacyStatus = request.PrivacyStatus,
            ChannelId = request.ChannelId,
            ChannelTitle = request.ChannelTitle,
            ItemCount = request.ItemCount,
            PublishedAtUtc = request.PublishedAtUtc,
            ThumbnailUrl = request.ThumbnailUrl,
            Etag = request.Etag,
            LastSyncedAtUtc = request.LastSyncedAtUtc,
            Metadata = request.Metadata
        };

        var created = await repository.CreateAsync(playlist, cancellationToken);
        var response = MapToResponse(created);
        return Results.Created($"/youtube-playlists/{response.Id}", response);
    }

    private static async Task<IResult> UpdateAsync(
        [FromRoute] Guid id,
        [FromBody] UpdateYoutubePlaylistRequest request,
        [FromServices] IYoutubePlaylistRepository repository,
        CancellationToken cancellationToken)
    {
        var playlist = await repository.GetByIdAsync(id, cancellationToken);
        if (playlist == null)
        {
            return Results.NotFound();
        }

        playlist.Title = request.Title;
        playlist.Description = request.Description;
        playlist.Status = request.Status;
        playlist.PrivacyStatus = request.PrivacyStatus;
        playlist.ChannelId = request.ChannelId;
        playlist.ChannelTitle = request.ChannelTitle;
        playlist.ItemCount = request.ItemCount;
        playlist.PublishedAtUtc = request.PublishedAtUtc;
        playlist.ThumbnailUrl = request.ThumbnailUrl;
        playlist.Etag = request.Etag;
        playlist.LastSyncedAtUtc = request.LastSyncedAtUtc;
        playlist.Metadata = request.Metadata;

        var updated = await repository.UpdateAsync(playlist, cancellationToken);
        return Results.Ok(MapToResponse(updated));
    }

    private static async Task<IResult> DeleteAsync(
        [FromRoute] Guid id,
        [FromServices] IYoutubePlaylistRepository repository,
        CancellationToken cancellationToken)
    {
        var deleted = await repository.DeleteAsync(id, cancellationToken);
        return deleted ? Results.NoContent() : Results.NotFound();
    }

    private static YoutubePlaylistResponse MapToResponse(YoutubePlaylist playlist)
    {
        return new YoutubePlaylistResponse(
            playlist.Id,
            playlist.YoutubePlaylistId,
            playlist.Title,
            playlist.Description,
            playlist.Status,
            playlist.PrivacyStatus,
            playlist.ChannelId,
            playlist.ChannelTitle,
            playlist.ItemCount,
            playlist.PublishedAtUtc,
            playlist.ThumbnailUrl,
            playlist.Etag,
            playlist.LastSyncedAtUtc,
            playlist.Metadata,
            playlist.CreatedAtUtc,
            playlist.UpdatedAtUtc
        );
    }
}
