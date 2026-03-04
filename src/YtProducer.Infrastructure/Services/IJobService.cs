using YtProducer.Domain.Entities;

namespace YtProducer.Infrastructure.Services;

public interface IJobService
{
    Task<Job?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<List<Job>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<List<Job>> GetByTargetAsync(string targetType, Guid targetId, CancellationToken cancellationToken = default);
    Task<List<Job>> GetByJobGroupIdAsync(Guid groupId, CancellationToken cancellationToken = default);
    Task<Job> CreateAsync(Job job, CancellationToken cancellationToken = default);
    Task UpdateAsync(Job job, CancellationToken cancellationToken = default);
    Task<Job?> AcquireNextJobAsync(string workerId, TimeSpan leaseDuration, CancellationToken cancellationToken = default);
    Task<bool> TryUpdateProgressAsync(Guid jobId, int progress, string workerId, CancellationToken cancellationToken = default);
    Task<bool> TryUpdateHeartbeatAsync(Guid jobId, string workerId, TimeSpan leaseDuration, CancellationToken cancellationToken = default);
    Task MarkCompletedAsync(Guid jobId, string? resultJson, string workerId, CancellationToken cancellationToken = default);
    Task MarkFailedAsync(Guid jobId, string? errorCode, string errorMessage, string workerId, CancellationToken cancellationToken = default);
    Task<int> RecoverExpiredLeasesAsync(CancellationToken cancellationToken = default);
}
