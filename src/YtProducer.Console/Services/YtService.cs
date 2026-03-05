using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using YtProducer.Contracts.Playlists;
using YtProducer.Contracts.YoutubePlaylists;
using YtProducer.Contracts.YoutubeUploadQueue;
using YtProducer.Domain.Entities;
using YtProducer.Domain.Enums;
using YtProducer.Infrastructure.Persistence;

namespace YtProducer.Console.Services;

/// <summary>
/// Working service that combines database operations with API testing.
/// Loads existing records and demonstrates API operations.
/// </summary>
public class YtService
{
    private readonly YtProducerDbContext _context;
    private readonly ApiClient _apiClient;
    private readonly ILogger<YtService> _logger;

    public YtService(
        YtProducerDbContext context,
        ApiClient apiClient,
        ILogger<YtService> logger)
    {
        _context = context;
        _apiClient = apiClient;
        _logger = logger;
    }

    public async Task RunAsync()
    {
        _logger.LogInformation("╔════════════════════════════════════════════════════╗");
        _logger.LogInformation("║       YtProducer Working Service - API Testing     ║");
        _logger.LogInformation("╚════════════════════════════════════════════════════╝\n");

        // Check API health first
        var apiHealthy = await _apiClient.HealthCheckAsync();
        if (!apiHealthy)
        {
            _logger.LogError("\n❌ API is not available. Start the API with 'dotnet run' in src/YtProducer.Api");
            return;
        }

        _logger.LogInformation("");

        // 1. Load existing data from database
        await LoadExistingDataAsync();

        // 2. Test API calls
        await TestApiCallsAsync();

        _logger.LogInformation("\n✓ Working service completed!");
    }

    private async Task LoadExistingDataAsync()
    {
        _logger.LogInformation("📊 STEP 1: Loading Existing Data from Database");
        _logger.LogInformation("═══════════════════════════════════════════════════\n");

        var playlistCount = await _context.Playlists.CountAsync();
        var youtubePlaylistCount = await _context.YoutubePlaylists.CountAsync();
        var uploadQueueCount = await _context.YoutubeUploadQueues.CountAsync();
        var jobCount = await _context.Jobs.CountAsync();

        _logger.LogInformation("  🎵 Playlists:            {Count}", playlistCount);
        _logger.LogInformation("  ▶️ YouTube Playlists:     {Count}", youtubePlaylistCount);
        _logger.LogInformation("  📤 Upload Queue Items:    {Count}", uploadQueueCount);
        _logger.LogInformation("  ⚙️ Jobs:                  {Count}\n", jobCount);

        // Load latest records
        if (playlistCount > 0)
        {
            var latestPlaylists = await _context.Playlists
                .OrderByDescending(p => p.CreatedAtUtc)
                .Take(3)
                .ToListAsync();

            _logger.LogInformation("  Recent Playlists:");
            foreach (var p in latestPlaylists)
            {
                _logger.LogInformation("    • {Title} ({Status})", p.Title, p.Status);
            }
            _logger.LogInformation("");
        }

        if (youtubePlaylistCount > 0)
        {
            var latestYtPlaylists = await _context.YoutubePlaylists
                .OrderByDescending(p => p.CreatedAtUtc)
                .Take(3)
                .ToListAsync();

            _logger.LogInformation("  Recent YouTube Playlists:");
            foreach (var p in latestYtPlaylists)
            {
                _logger.LogInformation("    • {Title} ({Privacy})", p.Title, p.PrivacyStatus);
            }
            _logger.LogInformation("");
        }
    }

    private async Task TestApiCallsAsync()
    {
        _logger.LogInformation("🌐 STEP 2: Testing API Calls");
        _logger.LogInformation("════════════════════════════\n");

        // Test 1: Get all YouTube playlists
        await TestGetAllYoutubePlaylistsAsync();

        _logger.LogInformation("");

        // Test 2: Create YouTube playlist
        await TestCreateYoutubePlaylistAsync();

        _logger.LogInformation("");

        // Test 3: Get upload queue items
        await TestGetUploadQueueAsync();

        _logger.LogInformation("");

        // Test 4: Create upload queue item
        await TestCreateUploadQueueAsync();

        _logger.LogInformation("");

        // Test 5: Get next pending upload
        await TestGetNextPendingUploadAsync();

        _logger.LogInformation("");

        // Test 6: Create playlist with tracks
        await TestCreatePlaylistAsync();
    }

    private async Task TestGetAllYoutubePlaylistsAsync()
    {
        _logger.LogInformation("TEST 1️⃣ : Get All YouTube Playlists");
        _logger.LogInformation("─────────────────────────────────────");

        var playlists = await _apiClient.GetYoutubePlaylistsAsync();

        if (playlists != null)
        {
            _logger.LogInformation("✓ Retrieved {Count} YouTube playlists from API", playlists.Count);
            if (playlists.Any())
            {
                foreach (var p in playlists.Take(3))
                {
                    _logger.LogInformation("  • {Title} (ID: {PlaylistId})", p.Title, p.YoutubePlaylistId);
                }
            }
        }
    }

    private async Task TestCreateYoutubePlaylistAsync()
    {
        _logger.LogInformation("TEST 2️⃣ : Create YouTube Playlist via API");
        _logger.LogInformation("──────────────────────────────────────────");

        var request = new CreateYoutubePlaylistRequest(
            YoutubePlaylistId: $"PLtest_{Guid.NewGuid().ToString().Substring(0, 8)}",
            Title: "API Test Playlist",
            Description: "Created by console app via API",
            Status: "Draft",
            PrivacyStatus: "private",
            ChannelId: "UCtest123",
            ChannelTitle: "Test Channel",
            ItemCount: 0,
            PublishedAtUtc: DateTime.UtcNow,
            ThumbnailUrl: null,
            Etag: null,
            LastSyncedAtUtc: null,
            Metadata: null
        );

        var created = await _apiClient.CreateYoutubePlaylistAsync(request);

        if (created != null)
        {
            _logger.LogInformation("✓ Created playlist: {Title}", created.Title);
            _logger.LogInformation("  ID: {Id}", created.Id);
            _logger.LogInformation("  YouTube ID: {YtId}", created.YoutubePlaylistId);

            // Test update
            await TestUpdateYoutubePlaylistAsync(created.Id);
        }
    }

    private async Task TestUpdateYoutubePlaylistAsync(Guid playlistId)
    {
        _logger.LogInformation("\n  → Updating playlist...");

        var updateRequest = new UpdateYoutubePlaylistRequest(
            Title: "API Test Playlist (Updated)",
            Description: "Updated via API call",
            Status: null,
            PrivacyStatus: "public",
            ChannelId: null,
            ChannelTitle: null,
            ItemCount: 5,
            PublishedAtUtc: null,
            ThumbnailUrl: null,
            Etag: null,
            LastSyncedAtUtc: null,
            Metadata: null
        );

        var updated = await _apiClient.UpdateYoutubePlaylistAsync(playlistId, updateRequest);

        if (updated != null)
        {
            _logger.LogInformation("  ✓ Updated: {Title}", updated.Title);
        }
    }

    private async Task TestGetUploadQueueAsync()
    {
        _logger.LogInformation("TEST 3️⃣ : Get Upload Queue Items");
        _logger.LogInformation("──────────────────────────────────");

        var queueItems = await _apiClient.GetUploadQueueAsync();

        if (queueItems != null)
        {
            _logger.LogInformation("✓ Retrieved {Count} items from upload queue", queueItems.Count);
            if (queueItems.Any())
            {
                foreach (var item in queueItems.Take(3))
                {
                    _logger.LogInformation("  • {Title} ({Status}) - Priority {Priority}",
                        item.Title, item.Status, item.Priority);
                }
            }
        }
    }

    private async Task TestCreateUploadQueueAsync()
    {
        _logger.LogInformation("TEST 4️⃣ : Create Upload Queue Item via API");
        _logger.LogInformation("───────────────────────────────────────────");

        var request = new CreateYoutubeUploadQueueRequest(
            Title: "API Test Video Upload",
            Description: "Testing queue creation from console app",
            Tags: new[] { "test", "api", "workout" },
            CategoryId: 10,
            VideoFilePath: "/test/sample-video.mp4",
            ThumbnailFilePath: "/test/sample-thumb.jpg",
            Priority: 1,
            ScheduledUploadAt: DateTime.UtcNow.AddHours(2),
            MaxAttempts: 3
        );

        var created = await _apiClient.CreateUploadQueueItemAsync(request);

        if (created != null)
        {
            _logger.LogInformation("✓ Created queue item: {Title}", created.Title);
            _logger.LogInformation("  ID: {Id}", created.Id);
            _logger.LogInformation("  Status: {Status}", created.Status);
            _logger.LogInformation("  Scheduled: {Time}", created.ScheduledUploadAt);

            // Test update
            await TestUpdateUploadQueueAsync(created.Id);
        }
    }

    private async Task TestUpdateUploadQueueAsync(Guid itemId)
    {
        _logger.LogInformation("\n  → Updating queue item...");

        var updateRequest = new UpdateYoutubeUploadQueueRequest(
            Status: "Pending",
            Priority: 2,
            Title: "API Test Video Upload (Updated)",
            Description: null,
            Tags: null,
            CategoryId: null,
            VideoFilePath: null,
            ThumbnailFilePath: null,
            ScheduledUploadAt: null,
            YoutubeVideoId: null,
            YoutubeUrl: null,
            Attempts: null,
            MaxAttempts: null,
            LastError: null
        );

        var updated = await _apiClient.UpdateUploadQueueItemAsync(itemId, updateRequest);

        if (updated != null)
        {
            _logger.LogInformation("  ✓ Updated: Priority {Priority}", updated.Priority);
        }
    }

    private async Task TestGetNextPendingUploadAsync()
    {
        _logger.LogInformation("TEST 5️⃣ : Get Next Pending Upload (Worker Query)");
        _logger.LogInformation("──────────────────────────────────────────────────");

        var nextItem = await _apiClient.GetNextPendingUploadAsync();

        if (nextItem != null)
        {
            _logger.LogInformation("✓ Next pending upload:");
            _logger.LogInformation("  Title: {Title}", nextItem.Title);
            _logger.LogInformation("  Priority: {Priority}", nextItem.Priority);
            _logger.LogInformation("  Status: {Status}", nextItem.Status);
        }
        else
        {
            _logger.LogInformation("ℹ️ No pending uploads in queue");
        }
    }

    private async Task TestCreatePlaylistAsync()
    {
        _logger.LogInformation("TEST 6️⃣ : Create Playlist with Tracks via API");
        _logger.LogInformation("──────────────────────────────────────────────");

        var tracks = new[]
        {
            new TrackData(
                PlaylistPosition: 1,
                Title: "Test Track 1",
                YouTubeTitle: "Test Track 1 - Official",
                Style: "Electronic",
                Duration: "3:45",
                TempoBpm: 128,
                Key: "C Minor",
                EnergyLevel: 8,
                Metadata: null
            ),
            new TrackData(
                PlaylistPosition: 2,
                Title: "Test Track 2",
                YouTubeTitle: "Test Track 2 - Remix",
                Style: "Electronic",
                Duration: "4:00",
                TempoBpm: 130,
                Key: "D Minor",
                EnergyLevel: 9,
                Metadata: null
            )
        };

        var request = new CreatePlaylistRequest(
            Title: "API Test Playlist",
            Theme: "Electronic",
            Description: "Playlist created via console app API call",
            PlaylistStrategy: "HighEnergy",
            Metadata: null,
            Tracks: tracks
        );

        var created = await _apiClient.CreatePlaylistAsync(request);

        if (created != null)
        {
            _logger.LogInformation("✓ Created playlist: {Title}", created.Title);
            _logger.LogInformation("  ID: {Id}", created.Id);
            _logger.LogInformation("  Track Count: {Count}", created.TrackCount);

            if (created.Tracks.Any())
            {
                _logger.LogInformation("  Tracks:");
                foreach (var track in created.Tracks)
                {
                    _logger.LogInformation("    • {Title}", track.Title);
                }
            }
        }
    }
}
