using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using YtProducer.Contracts.Jobs;
using YtProducer.Contracts.YoutubeVideoEngagements;
using YtProducer.Domain.Entities;
using YtProducer.Domain.Enums;
using YtProducer.Infrastructure.Persistence;
using YtProducer.Infrastructure.Services;

namespace YtProducer.Api.Endpoints;

public static class YoutubeVideoEngagementEndpoints
{
    public static IEndpointRouteBuilder MapYoutubeVideoEngagementEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/youtube-video-engagements").WithTags("YoutubeVideoEngagements");

        group.MapPost(string.Empty, CreateAsync)
            .Produces<YoutubeVideoEngagementResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest);

        group.MapGet(string.Empty, GetAllAsync)
            .Produces<IReadOnlyList<YoutubeVideoEngagementResponse>>(StatusCodes.Status200OK);

        group.MapGet("/{id:guid}", GetByIdAsync)
            .Produces<YoutubeVideoEngagementResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPut("/{id:guid}", UpdateAsync)
            .Produces<YoutubeVideoEngagementResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);

        group.MapDelete("/{id:guid}", DeleteAsync)
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/post", SchedulePostAsync)
            .Produces<ScheduleYoutubeVideoEngagementPostResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> CreateAsync(
        CreateYoutubeVideoEngagementRequest request,
        YtProducerDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var validation = ValidateCreateRequest(request.ChannelId, request.YoutubeVideoId, request.EngagementType, request.Status);
        if (validation is not null)
        {
            return Results.BadRequest(new { message = validation });
        }

        var now = DateTimeOffset.UtcNow;
        var entity = new YoutubeVideoEngagement
        {
            Id = Guid.NewGuid(),
            ChannelId = request.ChannelId.Trim(),
            YoutubeVideoId = request.YoutubeVideoId.Trim(),
            TrackId = request.TrackId,
            PlaylistId = request.PlaylistId,
            AlbumReleaseId = request.AlbumReleaseId,
            EngagementType = request.EngagementType.Trim(),
            PromptTemplateId = request.PromptTemplateId,
            PromptGenerationId = request.PromptGenerationId,
            Provider = NormalizeOptionalText(request.Provider),
            Model = NormalizeOptionalText(request.Model),
            GeneratedText = NormalizeOptionalText(request.GeneratedText),
            FinalText = NormalizeOptionalText(request.FinalText),
            Status = ParseStatus(request.Status) ?? YoutubeVideoEngagementStatus.Draft,
            YoutubeCommentId = NormalizeOptionalText(request.YoutubeCommentId),
            PostedAtUtc = request.PostedAtUtc,
            ErrorMessage = NormalizeOptionalText(request.ErrorMessage),
            MetadataJson = NormalizeJsonPayload(request.MetadataJson),
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        dbContext.YoutubeVideoEngagements.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.Created($"/youtube-video-engagements/{entity.Id}", Map(entity));
    }

    private static async Task<IResult> GetAllAsync(
        string? channelId,
        string? youtubeVideoId,
        Guid? trackId,
        Guid? playlistId,
        Guid? albumReleaseId,
        string? status,
        string? engagementType,
        YtProducerDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var query = dbContext.YoutubeVideoEngagements.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(channelId))
        {
            var normalized = channelId.Trim();
            query = query.Where(x => x.ChannelId == normalized);
        }

        if (!string.IsNullOrWhiteSpace(youtubeVideoId))
        {
            var normalized = youtubeVideoId.Trim();
            query = query.Where(x => x.YoutubeVideoId == normalized);
        }

        if (trackId.HasValue)
        {
            query = query.Where(x => x.TrackId == trackId.Value);
        }

        if (playlistId.HasValue)
        {
            query = query.Where(x => x.PlaylistId == playlistId.Value);
        }

        if (albumReleaseId.HasValue)
        {
            query = query.Where(x => x.AlbumReleaseId == albumReleaseId.Value);
        }

        if (!string.IsNullOrWhiteSpace(status) && ParseStatus(status) is { } parsedStatus)
        {
            query = query.Where(x => x.Status == parsedStatus);
        }

        if (!string.IsNullOrWhiteSpace(engagementType))
        {
            var normalized = engagementType.Trim();
            query = query.Where(x => x.EngagementType == normalized);
        }

        var items = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        return Results.Ok(items.Select(Map).ToList());
    }

    private static async Task<IResult> GetByIdAsync(
        Guid id,
        YtProducerDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var entity = await dbContext.YoutubeVideoEngagements
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        return entity is null ? Results.NotFound() : Results.Ok(Map(entity));
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        UpdateYoutubeVideoEngagementRequest request,
        YtProducerDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var entity = await dbContext.YoutubeVideoEngagements.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (entity is null)
        {
            return Results.NotFound();
        }

        if (request.ChannelId is not null)
        {
            if (string.IsNullOrWhiteSpace(request.ChannelId))
            {
                return Results.BadRequest(new { message = "ChannelId is required." });
            }

            entity.ChannelId = request.ChannelId.Trim();
        }

        if (request.YoutubeVideoId is not null)
        {
            if (string.IsNullOrWhiteSpace(request.YoutubeVideoId))
            {
                return Results.BadRequest(new { message = "YoutubeVideoId is required." });
            }

            entity.YoutubeVideoId = request.YoutubeVideoId.Trim();
        }

        if (request.EngagementType is not null)
        {
            if (string.IsNullOrWhiteSpace(request.EngagementType))
            {
                return Results.BadRequest(new { message = "EngagementType is required." });
            }

            entity.EngagementType = request.EngagementType.Trim();
        }

        if (request.Status is not null)
        {
            var parsedStatus = ParseStatus(request.Status);
            if (parsedStatus is null)
            {
                return Results.BadRequest(new { message = $"Unknown status: {request.Status}" });
            }

            entity.Status = parsedStatus.Value;
        }

        if (request.TrackId.HasValue) entity.TrackId = request.TrackId;
        if (request.PlaylistId.HasValue) entity.PlaylistId = request.PlaylistId;
        if (request.AlbumReleaseId.HasValue) entity.AlbumReleaseId = request.AlbumReleaseId;
        if (request.PromptTemplateId.HasValue) entity.PromptTemplateId = request.PromptTemplateId;
        if (request.PromptGenerationId.HasValue) entity.PromptGenerationId = request.PromptGenerationId;
        if (request.Provider is not null) entity.Provider = NormalizeOptionalText(request.Provider);
        if (request.Model is not null) entity.Model = NormalizeOptionalText(request.Model);
        if (request.GeneratedText is not null) entity.GeneratedText = NormalizeOptionalText(request.GeneratedText);
        if (request.FinalText is not null) entity.FinalText = NormalizeOptionalText(request.FinalText);
        if (request.YoutubeCommentId is not null) entity.YoutubeCommentId = NormalizeOptionalText(request.YoutubeCommentId);
        if (request.PostedAtUtc.HasValue) entity.PostedAtUtc = request.PostedAtUtc;
        if (request.ErrorMessage is not null) entity.ErrorMessage = NormalizeOptionalText(request.ErrorMessage);
        if (request.MetadataJson is not null) entity.MetadataJson = NormalizeJsonPayload(request.MetadataJson);

        entity.UpdatedAtUtc = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.Ok(Map(entity));
    }

    private static async Task<IResult> DeleteAsync(
        Guid id,
        YtProducerDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var entity = await dbContext.YoutubeVideoEngagements.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (entity is null)
        {
            return Results.NotFound();
        }

        dbContext.YoutubeVideoEngagements.Remove(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> SchedulePostAsync(
        Guid id,
        YtProducerDbContext dbContext,
        IJobService jobService,
        CancellationToken cancellationToken)
    {
        var engagement = await dbContext.YoutubeVideoEngagements
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (engagement is null)
        {
            return Results.NotFound();
        }

        if (string.IsNullOrWhiteSpace(engagement.YoutubeVideoId))
        {
            return Results.BadRequest(new { message = "YouTube video id is missing." });
        }

        if (string.IsNullOrWhiteSpace(engagement.FinalText))
        {
            return Results.BadRequest(new { message = "Final message is empty." });
        }

        if (!string.IsNullOrWhiteSpace(engagement.YoutubeCommentId))
        {
            return Results.BadRequest(new { message = "Comment already posted for this engagement." });
        }

        var payloadArguments = new CreatePostYoutubeEngagementCommentJobArguments(id);
        var payloadJson = JsonSerializer.Serialize(new ScheduledCommandPayload(
            "post-youtube-engagement-comment",
            1,
            JsonSerializer.SerializeToElement(payloadArguments)));

        var result = await jobService.CreateAsync(new Job
        {
            Type = JobType.PostYoutubeEngagementComment,
            TargetType = "youtube_video_engagement",
            TargetId = id,
            PayloadJson = payloadJson,
            MaxRetries = 3
        }, cancellationToken);

        var response = new ScheduleYoutubeVideoEngagementPostResponse(
            id,
            result.Job.Id,
            result.Job.Type.ToString(),
            result.Job.Status.ToString());

        return result.CreatedNew
            ? Results.Created($"/jobs/{result.Job.Id}", response)
            : Results.Ok(response);
    }

    private static YoutubeVideoEngagementResponse Map(YoutubeVideoEngagement entity)
        => new(
            entity.Id,
            entity.ChannelId,
            entity.YoutubeVideoId,
            entity.TrackId,
            entity.PlaylistId,
            entity.AlbumReleaseId,
            entity.EngagementType,
            entity.PromptTemplateId,
            entity.PromptGenerationId,
            entity.Provider,
            entity.Model,
            entity.GeneratedText,
            entity.FinalText,
            entity.Status.ToString(),
            entity.YoutubeCommentId,
            entity.PostedAtUtc,
            entity.ErrorMessage,
            entity.MetadataJson,
            entity.CreatedAtUtc,
            entity.UpdatedAtUtc);

    private static string? ValidateCreateRequest(string? channelId, string? youtubeVideoId, string? engagementType, string? status)
    {
        if (string.IsNullOrWhiteSpace(channelId))
        {
            return "ChannelId is required.";
        }

        if (string.IsNullOrWhiteSpace(youtubeVideoId))
        {
            return "YoutubeVideoId is required.";
        }

        if (string.IsNullOrWhiteSpace(engagementType))
        {
            return "EngagementType is required.";
        }

        if (status is not null && ParseStatus(status) is null)
        {
            return $"Unknown status: {status}";
        }

        return null;
    }

    private static YoutubeVideoEngagementStatus? ParseStatus(string? value)
        => Enum.TryParse<YoutubeVideoEngagementStatus>(value, true, out var status)
            ? status
            : null;

    private static string? NormalizeOptionalText(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string NormalizeJsonPayload(string? value)
        => string.IsNullOrWhiteSpace(value) ? "{}" : value.Trim();
}
