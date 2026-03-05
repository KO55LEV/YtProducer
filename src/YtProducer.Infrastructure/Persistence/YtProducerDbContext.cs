using Microsoft.EntityFrameworkCore;
using YtProducer.Domain.Entities;

namespace YtProducer.Infrastructure.Persistence;

public sealed class YtProducerDbContext : DbContext
{
    public YtProducerDbContext(DbContextOptions<YtProducerDbContext> options)
        : base(options)
    {
    }

    public DbSet<Playlist> Playlists => Set<Playlist>();

    public DbSet<Track> Tracks => Set<Track>();

    public DbSet<Job> Jobs => Set<Job>();

    public DbSet<YoutubePlaylist> YoutubePlaylists => Set<YoutubePlaylist>();

    public DbSet<YoutubeUploadQueue> YoutubeUploadQueues => Set<YoutubeUploadQueue>();

    public DbSet<TrackImage> TrackImages => Set<TrackImage>();

    public DbSet<TrackOnYoutube> TrackOnYoutube => Set<TrackOnYoutube>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(YtProducerDbContext).Assembly);
    }
}
