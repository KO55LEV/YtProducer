# Console App Quick Start Guide

## What is the Console App?

The **YtProducer.Console** is a command-line tool for rapid testing and development of the YtProducer backend. It uses the same database schema and dependency injection setup as the API and Worker, but runs synchronously without needing the full web server.

## Quick Start

### Prerequisites

1. **PostgreSQL running** on `localhost:5432`
2. **.env file configured** in project root with database credentials
3. **Database initialized** with schema from `/docker/postgres/init.sql`

### Running the Console App

**From PowerShell (Windows):**
```powershell
.\run-console.ps1
```

**From Bash (Linux/Mac):**
```bash
chmod +x run-console.sh
./run-console.sh
```

**Manually:**
```powershell
cd src/YtProducer.Console
dotnet run
```

## What It Does

The console app runs 6 demo operations:

1. **Query Existing Data** - Counts records in all tables
2. **Create Sample Playlist** - Inserts a music playlist
3. **Create YouTube Playlist** - Inserts a YouTube playlist
4. **Create Upload Queue Item** - Adds a video to the upload queue
5. **Create Job** - Creates a processing job
6. **List All Data** - Displays recent records with formatting

## Customizing for Your Tests

### Scenario 1: Test Playlist Creation Logic

Edit `src/YtProducer.Console/Services/DemoDataService.cs`:

```csharp
public async Task RunDemoAsync()
{
    await TestPlaylistCreationAsync();
}

private async Task TestPlaylistCreationAsync()
{
    _logger.LogInformation("Testing Playlist Creation");
    
    // Create playlist with specific logic
    var playlist = new Playlist
    {
        Id = Guid.NewGuid(),
        Title = "Test Playlist",
        Description = "Testing specific scenario",
        Status = PlaylistStatus.Draft,
        TrackCount = 5,
        Theme = "Rock",
        CreatedAtUtc = DateTime.UtcNow,
        UpdatedAtUtc = DateTime.UtcNow
    };
    
    _context.Playlists.Add(playlist);
    await _context.SaveChangesAsync();
    
    _logger.LogInformation("✓ Playlist created: {Id}", playlist.Id);
    
    // Test additional logic
    var retrieved = await _context.Playlists.FirstOrDefaultAsync(p => p.Id == playlist.Id);
    _logger.LogInformation("✓ Retrieved: {Title}", retrieved?.Title);
}
```

### Scenario 2: Test Upload Queue Processing

```csharp
private async Task TestUploadQueueProcessingAsync()
{
    _logger.LogInformation("Testing Upload Queue Processing");
    
    // Create a queue item
    var item = new YoutubeUploadQueue { /* ... */ };
    _context.YoutubeUploadQueues.Add(item);
    await _context.SaveChangesAsync();
    
    // Simulate worker retrieving next item
    var nextItem = await _context.YoutubeUploadQueues
        .Where(q => q.Status == YoutubeUploadStatus.Pending)
        .OrderBy(q => q.Priority)
        .FirstOrDefaultAsync();
    
    _logger.LogInformation("✓ Next item: {Title} (Priority: {Priority})", 
        nextItem?.Title, nextItem?.Priority);
    
    // Update status
    if (nextItem != null)
    {
        nextItem.Status = YoutubeUploadStatus.Uploading;
        nextItem.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        
        _logger.LogInformation("✓ Updated to: {Status}", nextItem.Status);
    }
}
```

### Scenario 3: Test Job Processing with Retries

```csharp
private async Task TestJobWithRetriesAsync()
{
    _logger.LogInformation("Testing Job Processing with Retries");
    
    var job = new Job
    {
        Id = Guid.NewGuid(),
        Type = JobType.GenerateMusic,
        Status = JobStatus.Active,
        Progress = 50,
        RetryCount = 0,
        MaxRetries = 3,
        CreatedAt = DateTime.UtcNow
    };
    
    _context.Jobs.Add(job);
    await _context.SaveChangesAsync();
    
    // Simulate retries
    for (int i = 0; i < 3; i++)
    {
        _logger.LogInformation("Attempt {Attempt}", i + 1);
        
        if (i == 2) // Fail on last attempt
        {
            job.Status = JobStatus.Failed;
            job.ErrorMessage = "Simulated failure";
        }
        else
        {
            job.RetryCount++;
            job.Progress += 30;
        }
        
        job.LastHeartbeat = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        
        await Task.Delay(1000); // Simulate work
    }
    
    _logger.LogInformation("✓ Job final status: {Status}", job.Status);
}
```

### Scenario 4: Test Track Creation and Metadata

```csharp
private async Task TestTrackCreationAsync()
{
    _logger.LogInformation("Testing Track Creation");
    
    var playlist = await _context.Playlists.FirstOrDefaultAsync();
    if (playlist == null)
    {
        _logger.LogWarning("No playlist found");
        return;
    }
    
    // Create a track with metadata
    var track = new Track
    {
        Id = Guid.NewGuid(),
        PlaylistId = playlist.Id,
        PlaylistPosition = 1,
        Title = "Test Track",
        YoutubTitle = "Test Track - Official",
        Status = TrackStatus.Draft,
        Creator = "Demo Artist",
        CreatedAtUtc = DateTime.UtcNow,
        UpdatedAtUtc = DateTime.UtcNow,
        Metadata = @"{
            ""style"": ""Rock"",
            ""tempo_bpm"": 120,
            ""energy_level"": ""High"",
            ""duration_seconds"": 180
        }"
    };
    
    _context.Tracks.Add(track);
    await _context.SaveChangesAsync();
    
    _logger.LogInformation("✓ Track created in playlist: {Title}", playlist.Title);
}
```

## Testing Database Queries

### Example: Test Complex Filtering

```csharp
private async Task TestComplexFiltersAsync()
{
    _logger.LogInformation("Testing Complex Filters");
    
    // Find pending uploads scheduled for the next hour
    var now = DateTime.UtcNow;
    var oneHourLater = now.AddHours(1);
    
    var scheduledItems = await _context.YoutubeUploadQueues
        .Where(q => q.Status == YoutubeUploadStatus.Pending)
        .Where(q => q.ScheduledUploadAt >= now && q.ScheduledUploadAt <= oneHourLater)
        .OrderBy(q => q.ScheduledUploadAt)
        .ToListAsync();
    
    _logger.LogInformation("✓ Found {Count} items scheduled for next hour", scheduledItems.Count);
    
    foreach (var item in scheduledItems)
    {
        _logger.LogInformation("  • {Title} @ {Time}", item.Title, item.ScheduledUploadAt);
    }
}
```

## Tips for Effective Testing

1. **Isolate Each Test** - Comment out other demos in `RunDemoAsync()`
2. **Use Logging** - All data operations log to console
3. **Leverage Timestamps** - Use `DateTime.UtcNow` for consistency
4. **Test Edge Cases** - Null values, empty collections, large datasets
5. **Verify Side Effects** - Query data after changes to confirm saves

## Environment Configuration for Testing

To change behavior without modifying code, edit `.env`:

```env
# Disable mock data for real DB testing
USE_MOCK_DATA=false

# Point to different database
POSTGRES_HOST=test-db.example.com
POSTGRES_DATABASE=ytproducer_test

# Adjust logging level
LOG_LEVEL=Debug
```

## Troubleshooting

**Connection refused:**
```
PostgreSQL server not running. Start it with:
docker-compose up -d postgres
```

**No tables found:**
```
Schema not initialized. Run init.sql:
docker exec ytproducer-postgres psql -U ytproducer -d ytproducer -f /docker-entrypoint-initdb.d/init.sql
```

**Package errors:**
```powershell
dotnet restore src/YtProducer.Console/YtProducer.Console.csproj
```

## Next Steps

- Add tests for specific features you're developing
- Create test data fixtures for integration tests
- Document new test scenarios as you discover them
- Use as a sandbox before deploying to API/Worker
