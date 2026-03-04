using YtProducer.Domain.Entities;

namespace YtProducer.Infrastructure.Services;

public interface IPlaylistRepository
{
    Task<IReadOnlyList<Playlist>> GetAllAsync(CancellationToken cancellationToken = default);
    
    Task<Playlist?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    
    Task<Playlist> CreateAsync(Playlist playlist, CancellationToken cancellationToken = default);
}
