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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(YtProducerDbContext).Assembly);
    }
}
