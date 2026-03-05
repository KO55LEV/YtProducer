using Microsoft.EntityFrameworkCore;
using YtProducer.Domain.Entities;
using YtProducer.Domain.Enums;
using YtProducer.Infrastructure.Persistence;

namespace YtProducer.Infrastructure.Services;

public sealed class YoutubeUploadQueueService : IYoutubeUploadQueueService
{
    private readonly YtProducerDbContext _context;
    private readonly ILogger<YoutubeUploadQueueService> _logger;

    public YoutubeUploadQueueService(
        YtProducerDbContext context,
        ILogger<YoutubeUploadQueueService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<YoutubeUploadQueue> CreateAsync(
        YoutubeUploadQueue queue,
        CancellationToken cancellationToken = default)
    {
        queue.Id = Guid.NewGuid();
        queue.CreatedAt = DateTimeOffset.UtcNow;
        queue.UpdatedAt = queue.CreatedAt;
        queue.Status = YoutubeUploadStatus.Pending;
        queue.Attempts = 0;

        _context.YoutubeUploadQueues.Add(queue);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Created Youtube upload queue {QueueId} for video {Title}",
            queue.Id,
            queue.Title);

        return queue;
    }

    public async Task<YoutubeUploadQueue?> GetAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        return await _context.YoutubeUploadQueues
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<YoutubeUploadQueue>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        return await _context.YoutubeUploadQueues
            .OrderByDescending(x => x.Priority)
            .ThenBy(x => x.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<YoutubeUploadQueue> UpdateAsync(
        YoutubeUploadQueue queue,
        CancellationToken cancellationToken = default)
    {
        queue.UpdatedAt = DateTimeOffset.UtcNow;
        _context.YoutubeUploadQueues.Update(queue);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Updated Youtube upload queue {QueueId}",
            queue.Id);

        return queue;
    }

    public async Task<bool> DeleteAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var queue = await _context.YoutubeUploadQueues
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (queue == null)
        {
            return false;
        }

        _context.YoutubeUploadQueues.Remove(queue);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Deleted Youtube upload queue {QueueId}",
            id);

        return true;
    }

    public async Task<YoutubeUploadQueue?> GetNextPendingAsync(
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;

        return await _context.YoutubeUploadQueues
            .Where(x => x.Status == YoutubeUploadStatus.Pending)
            .Where(x => x.ScheduledUploadAt == null || x.ScheduledUploadAt <= now)
            .OrderByDescending(x => x.Priority)
            .ThenBy(x => x.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
