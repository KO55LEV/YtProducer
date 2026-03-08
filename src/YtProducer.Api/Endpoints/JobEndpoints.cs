using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using YtProducer.Contracts.Jobs;
using YtProducer.Domain.Enums;
using YtProducer.Domain.Entities;
using YtProducer.Infrastructure.Persistence;
using YtProducer.Infrastructure.Services;

namespace YtProducer.Api.Endpoints;

public static class JobEndpoints
{
    public static IEndpointRouteBuilder MapJobEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/jobs").WithTags("Jobs");

        group.MapPost("/", CreateJobAsync);
        group.MapGet("/", GetAllJobsAsync);
        group.MapGet("/{id:guid}", GetJobByIdAsync);
        group.MapGet("/{id:guid}/logs", GetJobLogsAsync);
        group.MapGet("/track/{trackId:guid}", GetJobsByTrackIdAsync);
        group.MapGet("/group/{groupId:guid}", GetJobsByGroupIdAsync);
        group.MapPatch("/{id:guid}/progress", PatchProgressAsync);

        return app;
    }

    private static async Task<IResult> GetAllJobsAsync(
        [FromServices] IJobService jobService,
        CancellationToken cancellationToken)
    {
        var jobs = await jobService.GetAllAsync(cancellationToken);
        var response = jobs.Select(MapToJobResponse).ToList();
        return Results.Ok(response);
    }

    private static async Task<IResult> GetJobByIdAsync(
        [FromRoute] Guid id,
        [FromServices] IJobService jobService,
        CancellationToken cancellationToken)
    {
        var job = await jobService.GetByIdAsync(id, cancellationToken);
        
        if (job == null)
        {
            return Results.NotFound(new { message = $"Job {id} not found" });
        }

        return Results.Ok(MapToJobResponse(job));
    }

    private static async Task<IResult> GetJobsByTrackIdAsync(
        [FromRoute] Guid trackId,
        [FromServices] IJobService jobService,
        CancellationToken cancellationToken)
    {
        var jobs = await jobService.GetByTargetAsync("track", trackId, cancellationToken);
        var response = jobs.Select(MapToJobResponse).ToList();
        return Results.Ok(response);
    }

    private static async Task<IResult> GetJobLogsAsync(
        [FromRoute] Guid id,
        [FromServices] YtProducerDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var jobExists = await dbContext.Jobs
            .AsNoTracking()
            .AnyAsync(x => x.Id == id, cancellationToken);

        if (!jobExists)
        {
            return Results.NotFound(new { message = $"Job {id} not found" });
        }

        var logs = await dbContext.JobLogs
            .AsNoTracking()
            .Where(x => x.JobId == id)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Select(x => new JobLogResponse(
                x.Id,
                x.JobId,
                x.Level,
                x.Message,
                x.Metadata,
                x.CreatedAtUtc))
            .ToListAsync(cancellationToken);

        return Results.Ok(logs);
    }

    private static async Task<IResult> GetJobsByGroupIdAsync(
        [FromRoute] Guid groupId,
        [FromServices] IJobService jobService,
        CancellationToken cancellationToken)
    {
        var jobs = await jobService.GetByJobGroupIdAsync(groupId, cancellationToken);
        var response = jobs.Select(MapToJobResponse).ToList();
        return Results.Ok(response);
    }

    private static async Task<IResult> CreateJobAsync(
        [FromBody] CreateJobRequest request,
        [FromServices] IJobService jobService,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<JobType>(request.Type, ignoreCase: true, out var jobType))
        {
            return Results.BadRequest(new { message = $"Invalid job type: {request.Type}" });
        }

        var job = new Job
        {
            Type = jobType,
            TargetType = request.TargetType,
            TargetId = request.TargetId,
            JobGroupId = request.JobGroupId,
            Sequence = request.Sequence,
            PayloadJson = request.PayloadJson,
            IdempotencyKey = request.IdempotencyKey,
            MaxRetries = request.MaxRetries
        };

        var result = await jobService.CreateAsync(job, cancellationToken);
        return result.CreatedNew
            ? Results.Created($"/jobs/{result.Job.Id}", MapToJobResponse(result.Job))
            : Results.Ok(MapToJobResponse(result.Job));
    }

    private static async Task<IResult> PatchProgressAsync(
        [FromRoute] Guid id,
        [FromBody] UpdateProgressRequest request,
        [FromServices] IJobService jobService,
        CancellationToken cancellationToken)
    {
        if (request.Progress is < 0 or > 100)
        {
            return Results.BadRequest(new { message = "Progress must be between 0 and 100" });
        }

        if (string.IsNullOrWhiteSpace(request.WorkerId))
        {
            return Results.BadRequest(new { message = "workerId is required" });
        }

        var updated = await jobService.TryUpdateProgressAsync(id, request.Progress, request.WorkerId!, cancellationToken);
        if (!updated)
        {
            return Results.NotFound(new { message = $"Running job {id} for worker {request.WorkerId} not found" });
        }

        return Results.Ok(new { id, progress = request.Progress, workerId = request.WorkerId });
    }

    private static JobResponse MapToJobResponse(Job job)
    {
        return new JobResponse(
            job.Id,
            job.Type.ToString(),
            job.Status.ToString(),
            job.TargetType,
            job.TargetId,
            job.JobGroupId,
            job.Sequence,
            job.Progress,
            job.PayloadJson,
            job.ResultJson,
            job.RetryCount,
            job.MaxRetries,
            job.WorkerId,
            job.LeaseExpiresAt,
            job.CreatedAt,
            job.StartedAt,
            job.FinishedAt,
            job.LastHeartbeat,
            job.ErrorCode,
            job.ErrorMessage,
            job.IdempotencyKey
        );
    }
}
