using Microsoft.EntityFrameworkCore;
using YtProducer.Domain.Entities;
using YtProducer.Infrastructure.Persistence;

namespace YtProducer.Infrastructure.Services;

public sealed class PlaylistRepository : IPlaylistRepository
{
    private readonly YtProducerDbContext _context;

    public PlaylistRepository(YtProducerDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<Playlist>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var playlists = await _context.Playlists
            .Include(x => x.Tracks)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        foreach (var playlist in playlists)
        {
            playlist.Tracks = playlist.Tracks
                .OrderBy(x => x.PlaylistPosition)
                .ToList();
        }

        return playlists;
    }

    public async Task<Playlist?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var playlist = await _context.Playlists
            .Include(x => x.Tracks)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (playlist is null)
        {
            return null;
        }

        playlist.Tracks = playlist.Tracks
            .OrderBy(x => x.PlaylistPosition)
            .ToList();

        return playlist;
    }

    public async Task<Playlist> CreateAsync(Playlist playlist, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;

        playlist.Id = Guid.NewGuid();
        playlist.CreatedAtUtc = now;
        playlist.UpdatedAtUtc = now;
        playlist.TrackCount = playlist.Tracks.Count;

        var tracks = playlist.Tracks.ToList();
        var usedPositions = new HashSet<int>();
        var nextAvailablePosition = 1;

        for (var index = 0; index < tracks.Count; index++)
        {
            var track = tracks[index];
            track.Id = Guid.NewGuid();
            track.PlaylistId = playlist.Id;
            track.CreatedAtUtc = now;
            track.UpdatedAtUtc = now;

            if (track.PlaylistPosition > 0 && usedPositions.Add(track.PlaylistPosition))
            {
                continue;
            }

            while (usedPositions.Contains(nextAvailablePosition))
            {
                nextAvailablePosition++;
            }

            track.PlaylistPosition = nextAvailablePosition;
            usedPositions.Add(track.PlaylistPosition);
            nextAvailablePosition++;
        }

        _context.Playlists.Add(playlist);
        await _context.SaveChangesAsync(cancellationToken);

        playlist.Tracks = tracks
            .OrderBy(x => x.PlaylistPosition)
            .ToList();

        return playlist;
    }
}
