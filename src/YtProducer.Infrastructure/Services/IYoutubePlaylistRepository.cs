using YtProducer.Domain.Entities;

namespace YtProducer.Infrastructure.Services;

public interface IYoutubePlaylistRepository
{
    Task<IReadOnlyList<YoutubePlaylist>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<YoutubePlaylist?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<YoutubePlaylist> CreateAsync(YoutubePlaylist playlist, CancellationToken cancellationToken = default);

    Task<YoutubePlaylist> UpdateAsync(YoutubePlaylist playlist, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
