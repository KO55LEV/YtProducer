using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using YtProducer.Domain.Entities;
using YtProducer.Domain.Enums;
using YtProducer.Infrastructure.Persistence;

namespace YtProducer.Infrastructure.Services;

public class JobService : IJobService
{
    private readonly YtProducerDbContext _context;
    private readonly ILogger<JobService> _logger;

    public JobService(YtProducerDbContext context, ILogger<JobService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<Job?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Jobs.FirstOrDefaultAsync(j => j.Id == id, cancellationToken);
    }

    public async Task<List<Job>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Jobs
            .OrderByDescending(j => j.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<Job>> GetByTargetAsync(string targetType, Guid targetId, CancellationToken cancellationToken = default)
    {
        return await _context.Jobs
            .Where(j => j.TargetType == targetType && j.TargetId == targetId)
            .OrderBy(j => j.Sequence)
            .ThenBy(j => j.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<Job>> GetByJobGroupIdAsync(Guid groupId, CancellationToken cancellationToken = default)
    {
        return await _context.Jobs
            .Where(j => j.JobGroupId == groupId)
            .OrderBy(j => j.Sequence)
            .ThenBy(j => j.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<JobCreateResult> CreateAsync(Job job, CancellationToken cancellationToken = default)
    {
        var normalizedPayload = NormalizeJson(job.PayloadJson);
        job.PayloadJson = normalizedPayload;
        job.IdempotencyKey ??= ComputeIdempotencyKey(job.Type, job.TargetType, job.TargetId, normalizedPayload);

        var existing = await _context.Jobs
            .FirstOrDefaultAsync(x => x.IdempotencyKey != null && x.IdempotencyKey == job.IdempotencyKey, cancellationToken);

        if (existing is not null)
        {
            return new JobCreateResult(existing, false);
        }

        job.Id = Guid.NewGuid();
        job.CreatedAt = DateTimeOffset.UtcNow;
        job.Status = Enum.IsDefined(job.Status) ? job.Status : JobStatus.Pending;
        job.Progress = 0;
        job.RetryCount = Math.Max(job.RetryCount, 0);
        job.MaxRetries = Math.Max(job.MaxRetries, 1);

        _context.Jobs.Add(job);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Created job {JobId} type {JobType} target {TargetType}:{TargetId}", 
            job.Id, job.Type, job.TargetType, job.TargetId);

        return new JobCreateResult(job, true);
    }

    public async Task UpdateAsync(Job job, CancellationToken cancellationToken = default)
    {
        _context.Jobs.Update(job);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<Job?> AcquireNextJobAsync(string workerId, TimeSpan leaseDuration, CancellationToken cancellationToken = default)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var job = await _context.Jobs
                .FromSqlRaw(@"
SELECT * FROM jobs
WHERE status IN ('Pending', 'Retrying')
ORDER BY created_at
LIMIT 1
FOR UPDATE SKIP LOCKED")
                .FirstOrDefaultAsync(cancellationToken);

            if (job == null)
            {
                await transaction.CommitAsync(cancellationToken);
                return null;
            }

            job.Status = JobStatus.Running;
            job.WorkerId = workerId;
            job.StartedAt ??= DateTimeOffset.UtcNow;
            job.LastHeartbeat = DateTimeOffset.UtcNow;
            job.LeaseExpiresAt = DateTimeOffset.UtcNow.Add(leaseDuration);
            job.ErrorCode = null;
            job.ErrorMessage = null;

            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            _logger.LogInformation("Worker {WorkerId} leased job {JobId} until {LeaseExpiresAt}", 
                workerId, job.Id, job.LeaseExpiresAt);

            return job;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to dequeue job for worker {WorkerId}", workerId);
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<bool> TryUpdateProgressAsync(Guid jobId, int progress, string workerId, CancellationToken cancellationToken = default)
    {
        var normalized = Math.Clamp(progress, 0, 100);
        var now = DateTimeOffset.UtcNow;

        var affected = await _context.Jobs
            .Where(j => j.Id == jobId && j.Status == JobStatus.Running && j.WorkerId == workerId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(j => j.Progress, normalized)
                .SetProperty(j => j.LastHeartbeat, now), cancellationToken);

        return affected > 0;
    }

    public async Task<bool> TryUpdateHeartbeatAsync(Guid jobId, string workerId, TimeSpan leaseDuration, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var leaseExpiresAt = now.Add(leaseDuration);

        var affected = await _context.Jobs
            .Where(j => j.Id == jobId && j.Status == JobStatus.Running && j.WorkerId == workerId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(j => j.LastHeartbeat, now)
                .SetProperty(j => j.LeaseExpiresAt, leaseExpiresAt), cancellationToken);

        return affected > 0;
    }

    public async Task MarkCompletedAsync(Guid jobId, string? resultJson, string workerId, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var affected = await _context.Jobs
            .Where(j => j.Id == jobId && j.Status == JobStatus.Running && j.WorkerId == workerId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(j => j.Status, JobStatus.Completed)
                .SetProperty(j => j.Progress, 100)
                .SetProperty(j => j.ResultJson, resultJson)
                .SetProperty(j => j.FinishedAt, now)
                .SetProperty(j => j.LeaseExpiresAt, (DateTimeOffset?)null)
                .SetProperty(j => j.LastHeartbeat, now), cancellationToken);

        if (affected == 0)
        {
            throw new InvalidOperationException($"Job {jobId} completion update failed for worker {workerId}");
        }
    }

    public async Task MarkFailedAsync(Guid jobId, string? errorCode, string errorMessage, string workerId, CancellationToken cancellationToken = default)
    {
        var job = await _context.Jobs.FirstOrDefaultAsync(j => j.Id == jobId, cancellationToken)
            ?? throw new InvalidOperationException($"Job {jobId} not found");

        if (job.Status != JobStatus.Running || job.WorkerId != workerId)
        {
            return;
        }

        job.RetryCount += 1;
        job.ErrorCode = errorCode;
        job.ErrorMessage = errorMessage;
        job.FinishedAt = DateTimeOffset.UtcNow;
        job.LeaseExpiresAt = null;

        if (job.RetryCount < job.MaxRetries)
        {
            job.Status = JobStatus.Retrying;
            job.WorkerId = null;
            job.StartedAt = null;
        }
        else
        {
            job.Status = JobStatus.Failed;
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> RecoverExpiredLeasesAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;

        var recoverable = await _context.Jobs
            .Where(j => j.Status == JobStatus.Running && j.LeaseExpiresAt != null && j.LeaseExpiresAt < now)
            .ToListAsync(cancellationToken);

        foreach (var job in recoverable)
        {
            job.RetryCount += 1;
            job.WorkerId = null;
            job.StartedAt = null;
            job.LeaseExpiresAt = null;
            job.ErrorCode = "LeaseExpired";
            job.ErrorMessage = "Job lease expired before completion";
            job.Status = job.RetryCount < job.MaxRetries ? JobStatus.Pending : JobStatus.Failed;
        }

        if (recoverable.Count > 0)
        {
            await _context.SaveChangesAsync(cancellationToken);
            _logger.LogWarning("Recovered {Count} expired leased jobs", recoverable.Count);
        }

        return recoverable.Count;
    }

    private static string ComputeIdempotencyKey(JobType type, string? targetType, Guid? targetId, string? normalizedPayload)
    {
        var basis = $"{type}|{targetType?.ToLowerInvariant()}|{targetId?.ToString() ?? string.Empty}|{normalizedPayload ?? string.Empty}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(basis));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string? NormalizeJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return json;
        }

        try
        {
            var node = JsonNode.Parse(json);
            if (node is null)
            {
                return json;
            }

            var normalized = NormalizeNode(node);
            return normalized.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
        }
        catch (JsonException)
        {
            return json;
        }
    }

    private static JsonNode NormalizeNode(JsonNode node)
    {
        return node switch
        {
            JsonObject obj => NormalizeObject(obj),
            JsonArray arr => NormalizeArray(arr),
            _ => node.DeepClone()
        };
    }

    private static JsonObject NormalizeObject(JsonObject obj)
    {
        var normalized = new JsonObject();
        foreach (var kv in obj.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            normalized[kv.Key] = kv.Value is null ? null : NormalizeNode(kv.Value);
        }

        return normalized;
    }

    private static JsonArray NormalizeArray(JsonArray arr)
    {
        var normalized = new JsonArray();
        foreach (var item in arr)
        {
            normalized.Add(item is null ? null : NormalizeNode(item));
        }

        return normalized;
    }
}
