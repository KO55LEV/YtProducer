using YtProducer.Domain.Entities;

namespace YtProducer.Infrastructure.Services;

public interface IJobQueueService
{
    Task<IReadOnlyList<Job>> GetPendingJobsAsync(int batchSize, CancellationToken cancellationToken);

    Task MarkInProgressAsync(Guid jobId, CancellationToken cancellationToken);

    Task MarkCompletedAsync(Guid jobId, CancellationToken cancellationToken);

    Task MarkFailedAsync(Guid jobId, string errorMessage, CancellationToken cancellationToken);
}
