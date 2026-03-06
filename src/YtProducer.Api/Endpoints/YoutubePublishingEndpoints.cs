using Microsoft.EntityFrameworkCore;
using YtProducer.Contracts.YoutubePublishing;
using YtProducer.Domain.Entities;
using YtProducer.Infrastructure.Persistence;

namespace YtProducer.Api.Endpoints;

public static class YoutubePublishingEndpoints
{
    public static IEndpointRouteBuilder MapYoutubePublishingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/youtube-publish-state").WithTags("YouTube Publishing");

        group.MapGet(string.Empty, GetAsync)
            .WithName("GetYoutubePublishState")
            .Produces<YoutubeLastPublishedDateResponse>(StatusCodes.Status200OK);

        group.MapPut(string.Empty, UpdateAsync)
            .WithName("UpdateYoutubePublishState")
            .Produces<YoutubeLastPublishedDateResponse>(StatusCodes.Status200OK);

        return app;
    }

    private static async Task<IResult> GetAsync(
        YtProducerDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var state = await dbContext.YoutubeLastPublishedDates
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == 1, cancellationToken);

        if (state == null)
        {
            state = new YoutubeLastPublishedDate
            {
                Id = 1,
                LastPublishedDate = new DateTimeOffset(2026, 3, 8, 8, 0, 0, TimeSpan.Zero),
                VideoId = null
            };
            dbContext.Add(state);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return Results.Ok(new YoutubeLastPublishedDateResponse(
            state.Id,
            state.LastPublishedDate,
            state.VideoId));
    }

    private static async Task<IResult> UpdateAsync(
        UpdateYoutubeLastPublishedDateRequest request,
        YtProducerDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var state = await dbContext.YoutubeLastPublishedDates
            .FirstOrDefaultAsync(x => x.Id == 1, cancellationToken);

        if (state == null)
        {
            state = new YoutubeLastPublishedDate { Id = 1 };
            dbContext.Add(state);
        }

        state.LastPublishedDate = request.LastPublishedDate;
        state.VideoId = string.IsNullOrWhiteSpace(request.VideoId) ? null : request.VideoId.Trim();
        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.Ok(new YoutubeLastPublishedDateResponse(
            state.Id,
            state.LastPublishedDate,
            state.VideoId));
    }
}
