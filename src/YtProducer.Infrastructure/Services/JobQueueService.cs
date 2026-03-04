using Microsoft.EntityFrameworkCore;
using YtProducer.Domain.Entities;
using YtProducer.Domain.Enums;
using YtProducer.Infrastructure.Persistence;

namespace YtProducer.Infrastructure.Services;

public sealed class JobQueueService : IJobQueueService
{
    private readonly YtProducerDbContext _dbContext;

    public JobQueueService(YtProducerDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<Job>> GetPendingJobsAsync(int batchSize, CancellationToken cancellationToken)
    {
        return await _dbContext.Jobs
            .AsNoTracking()
            .Where(x => x.Status == JobStatus.Pending)
            .OrderBy(x => x.CreatedAtUtc)
            .Take(batchSize)
            .ToListAsync(cancellationToken);
    }

    public async Task MarkInProgressAsync(Guid jobId, CancellationToken cancellationToken)
    {
        var job = await _dbContext.Jobs
            .SingleOrDefaultAsync(x => x.Id == jobId, cancellationToken);

        if (job is null)
        {
            return;
        }

        job.Status = JobStatus.InProgress;
        job.StartedAtUtc = DateTimeOffset.UtcNow;
        job.Attempts += 1;

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkCompletedAsync(Guid jobId, CancellationToken cancellationToken)
    {
        var job = await _dbContext.Jobs
            .SingleOrDefaultAsync(x => x.Id == jobId, cancellationToken);

        if (job is null)
        {
            return;
        }

        job.Status = JobStatus.Completed;
        job.CompletedAtUtc = DateTimeOffset.UtcNow;
        job.ErrorMessage = null;

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkFailedAsync(Guid jobId, string errorMessage, CancellationToken cancellationToken)
    {
        var job = await _dbContext.Jobs
            .SingleOrDefaultAsync(x => x.Id == jobId, cancellationToken);

        if (job is null)
        {
            return;
        }

        job.Status = JobStatus.Failed;
        job.CompletedAtUtc = DateTimeOffset.UtcNow;
        job.ErrorMessage = errorMessage;

        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
