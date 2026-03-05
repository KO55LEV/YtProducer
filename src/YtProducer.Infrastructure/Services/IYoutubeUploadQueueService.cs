using YtProducer.Domain.Entities;

namespace YtProducer.Infrastructure.Services;

public interface IYoutubeUploadQueueService
{
    Task<YoutubeUploadQueue> CreateAsync(YoutubeUploadQueue queue, CancellationToken cancellationToken = default);

    Task<YoutubeUploadQueue?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<YoutubeUploadQueue>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<YoutubeUploadQueue> UpdateAsync(YoutubeUploadQueue queue, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    Task<YoutubeUploadQueue?> GetNextPendingAsync(CancellationToken cancellationToken = default);
}
