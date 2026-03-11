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

    public DbSet<JobLog> JobLogs => Set<JobLog>();

    public DbSet<YoutubePlaylist> YoutubePlaylists => Set<YoutubePlaylist>();

    public DbSet<YoutubeUploadQueue> YoutubeUploadQueues => Set<YoutubeUploadQueue>();

    public DbSet<TrackImage> TrackImages => Set<TrackImage>();

    public DbSet<TrackSocialStat> TrackSocialStats => Set<TrackSocialStat>();

    public DbSet<TrackOnYoutube> TrackOnYoutube => Set<TrackOnYoutube>();

    public DbSet<TrackVideoGeneration> TrackVideoGenerations => Set<TrackVideoGeneration>();

    public DbSet<TrackLoop> TrackLoops => Set<TrackLoop>();

    public DbSet<AlbumRelease> AlbumReleases => Set<AlbumRelease>();

    public DbSet<YoutubeLastPublishedDate> YoutubeLastPublishedDates => Set<YoutubeLastPublishedDate>();

    public DbSet<PromptTemplate> PromptTemplates => Set<PromptTemplate>();

    public DbSet<PromptGeneration> PromptGenerations => Set<PromptGeneration>();

    public DbSet<PromptGenerationOutput> PromptGenerationOutputs => Set<PromptGenerationOutput>();

    public DbSet<YoutubeVideoEngagement> YoutubeVideoEngagements => Set<YoutubeVideoEngagement>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(YtProducerDbContext).Assembly);
    }
}
