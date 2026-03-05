using Microsoft.EntityFrameworkCore;
using YtProducer.Domain.Entities;
using YtProducer.Infrastructure.Persistence;

namespace YtProducer.Infrastructure.Services;

public sealed class YoutubePlaylistRepository : IYoutubePlaylistRepository
{
    private readonly YtProducerDbContext _context;

    public YoutubePlaylistRepository(YtProducerDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<YoutubePlaylist>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _context.YoutubePlaylists
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);
    }

    public async Task<YoutubePlaylist?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.YoutubePlaylists.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    public async Task<YoutubePlaylist> CreateAsync(YoutubePlaylist playlist, CancellationToken cancellationToken = default)
    {
        playlist.Id = Guid.NewGuid();
        playlist.CreatedAtUtc = DateTimeOffset.UtcNow;
        playlist.UpdatedAtUtc = playlist.CreatedAtUtc;

        _context.YoutubePlaylists.Add(playlist);
        await _context.SaveChangesAsync(cancellationToken);

        return playlist;
    }

    public async Task<YoutubePlaylist> UpdateAsync(YoutubePlaylist playlist, CancellationToken cancellationToken = default)
    {
        playlist.UpdatedAtUtc = DateTimeOffset.UtcNow;
        _context.YoutubePlaylists.Update(playlist);
        await _context.SaveChangesAsync(cancellationToken);

        return playlist;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var playlist = await _context.YoutubePlaylists.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (playlist == null)
        {
            return false;
        }

        _context.YoutubePlaylists.Remove(playlist);
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }
}
