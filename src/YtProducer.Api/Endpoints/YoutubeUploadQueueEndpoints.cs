using Microsoft.AspNetCore.Mvc;
using YtProducer.Contracts.YoutubeUploadQueue;
using YtProducer.Domain.Entities;
using YtProducer.Domain.Enums;
using YtProducer.Infrastructure.Services;

namespace YtProducer.Api.Endpoints;

public static class YoutubeUploadQueueEndpoints
{
    public static IEndpointRouteBuilder MapYoutubeUploadQueueEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/youtube-upload-queue").WithTags("YoutubeUploadQueue");

        group.MapPost(string.Empty, CreateAsync)
            .WithName("CreateYoutubeUploadQueue")
            .Produces<YoutubeUploadQueueResponse>(StatusCodes.Status201Created);

        group.MapGet(string.Empty, GetAllAsync)
            .WithName("GetYoutubeUploadQueues")
            .Produces<IReadOnlyList<YoutubeUploadQueueResponse>>(StatusCodes.Status200OK);

        group.MapGet("/{id:guid}", GetByIdAsync)
            .WithName("GetYoutubeUploadQueueById")
            .Produces<YoutubeUploadQueueResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPut("/{id:guid}", UpdateAsync)
            .WithName("UpdateYoutubeUploadQueue")
            .Produces<YoutubeUploadQueueResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        group.MapDelete("/{id:guid}", DeleteAsync)
            .WithName("DeleteYoutubeUploadQueue")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/next", GetNextPendingAsync)
            .WithName("GetNextPendingYoutubeUpload")
            .Produces<YoutubeUploadQueueResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> CreateAsync(
        [FromBody] CreateYoutubeUploadQueueRequest request,
        [FromServices] IYoutubeUploadQueueService service,
        CancellationToken cancellationToken)
    {
        var queue = new YoutubeUploadQueue
        {
            Title = request.Title,
            Description = request.Description,
            Tags = request.Tags,
            CategoryId = request.CategoryId ?? 10,
            VideoFilePath = request.VideoFilePath,
            ThumbnailFilePath = request.ThumbnailFilePath,
            Priority = request.Priority ?? 0,
            ScheduledUploadAt = request.ScheduledUploadAt,
            MaxAttempts = request.MaxAttempts ?? 5
        };

        var created = await service.CreateAsync(queue, cancellationToken);
        var response = MapToResponse(created);

        return Results.Created($"/youtube-upload-queue/{response.Id}", response);
    }

    private static async Task<IResult> GetAllAsync(
        [FromServices] IYoutubeUploadQueueService service,
        CancellationToken cancellationToken)
    {
        var queues = await service.GetAllAsync(cancellationToken);
        var response = queues.Select(MapToResponse).ToList();
        return Results.Ok(response);
    }

    private static async Task<IResult> GetByIdAsync(
        [FromRoute] Guid id,
        [FromServices] IYoutubeUploadQueueService service,
        CancellationToken cancellationToken)
    {
        var queue = await service.GetAsync(id, cancellationToken);
        if (queue == null)
        {
            return Results.NotFound();
        }

        return Results.Ok(MapToResponse(queue));
    }

    private static async Task<IResult> UpdateAsync(
        [FromRoute] Guid id,
        [FromBody] UpdateYoutubeUploadQueueRequest request,
        [FromServices] IYoutubeUploadQueueService service,
        CancellationToken cancellationToken)
    {
        var queue = await service.GetAsync(id, cancellationToken);
        if (queue == null)
        {
            return Results.NotFound();
        }

        if (request.Title != null) queue.Title = request.Title;
        if (request.Description != null) queue.Description = request.Description;
        if (request.Tags != null) queue.Tags = request.Tags;
        if (request.CategoryId.HasValue) queue.CategoryId = request.CategoryId.Value;
        if (request.VideoFilePath != null) queue.VideoFilePath = request.VideoFilePath;
        if (request.ThumbnailFilePath != null) queue.ThumbnailFilePath = request.ThumbnailFilePath;
        if (request.Priority.HasValue) queue.Priority = request.Priority.Value;
        if (request.ScheduledUploadAt.HasValue) queue.ScheduledUploadAt = request.ScheduledUploadAt;
        if (request.MaxAttempts.HasValue) queue.MaxAttempts = request.MaxAttempts.Value;
        if (request.YoutubeVideoId != null) queue.YoutubeVideoId = request.YoutubeVideoId;
        if (request.YoutubeUrl != null) queue.YoutubeUrl = request.YoutubeUrl;
        if (request.Attempts.HasValue) queue.Attempts = request.Attempts.Value;
        if (request.LastError != null) queue.LastError = request.LastError;

        if (request.Status != null && Enum.TryParse<YoutubeUploadStatus>(request.Status, ignoreCase: true, out var status))
        {
            queue.Status = status;
        }

        var updated = await service.UpdateAsync(queue, cancellationToken);
        return Results.Ok(MapToResponse(updated));
    }

    private static async Task<IResult> DeleteAsync(
        [FromRoute] Guid id,
        [FromServices] IYoutubeUploadQueueService service,
        CancellationToken cancellationToken)
    {
        var deleted = await service.DeleteAsync(id, cancellationToken);
        return deleted ? Results.NoContent() : Results.NotFound();
    }

    private static async Task<IResult> GetNextPendingAsync(
        [FromServices] IYoutubeUploadQueueService service,
        CancellationToken cancellationToken)
    {
        var queue = await service.GetNextPendingAsync(cancellationToken);
        if (queue == null)
        {
            return Results.NotFound(new { message = "No pending uploads available" });
        }

        return Results.Ok(MapToResponse(queue));
    }

    private static YoutubeUploadQueueResponse MapToResponse(YoutubeUploadQueue queue)
    {
        return new YoutubeUploadQueueResponse(
            queue.Id,
            queue.Status.ToString(),
            queue.Priority,
            queue.Title,
            queue.Description,
            queue.Tags,
            queue.CategoryId,
            queue.VideoFilePath,
            queue.ThumbnailFilePath,
            queue.YoutubeVideoId,
            queue.YoutubeUrl,
            queue.ScheduledUploadAt,
            queue.Attempts,
            queue.MaxAttempts,
            queue.LastError,
            queue.CreatedAt,
            queue.UpdatedAt
        );
    }
}
