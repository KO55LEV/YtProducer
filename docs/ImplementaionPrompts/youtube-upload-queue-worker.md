# YouTube Upload Queue - Worker Query Example

## Fetch Next Pending Job

This query retrieves the next video upload job that should be processed by the worker.

### Criteria:
- Status must be `Pending`
- ScheduledUploadAt must be null OR <= current UTC time

### Ordering:
1. Priority (DESC) - Higher priority jobs first
2. CreatedAt (ASC) - Older jobs first within same priority

### C# EF Core Query:

```csharp
var now = DateTimeOffset.UtcNow;

var nextJob = await _context.YoutubeUploadQueues
    .Where(x => x.Status == YoutubeUploadStatus.Pending)
    .Where(x => x.ScheduledUploadAt == null || x.ScheduledUploadAt <= now)
    .OrderByDescending(x => x.Priority)
    .ThenBy(x => x.CreatedAt)
    .FirstOrDefaultAsync(cancellationToken);
```

### Alternative: Using the Service

```csharp
var nextJob = await _youtubeUploadQueueService.GetNextPendingAsync(cancellationToken);
```

### Worker Processing Flow:

1. Fetch next pending job
2. Update status to `Uploading`
3. Upload video to YouTube
4. On success:
   - Set `YoutubeVideoId` and `YoutubeUrl`
   - Set status to `Uploaded`
5. On failure:
   - Increment `Attempts`
   - Set `LastError`
   - If `Attempts >= MaxAttempts`, set status to `Failed`
   - Otherwise, keep status as `Pending` for retry

### Example Worker Implementation:

```csharp
public class YoutubeUploadWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<YoutubeUploadWorker> _logger;

    public YoutubeUploadWorker(
        IServiceProvider serviceProvider,
        ILogger<YoutubeUploadWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var service = scope.ServiceProvider
                    .GetRequiredService<IYoutubeUploadQueueService>();

                var job = await service.GetNextPendingAsync(stoppingToken);

                if (job != null)
                {
                    await ProcessUploadAsync(job, service, stoppingToken);
                }
                else
                {
                    // No pending jobs, wait before checking again
                    await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in upload worker");
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }
    }

    private async Task ProcessUploadAsync(
        YoutubeUploadQueue job,
        IYoutubeUploadQueueService service,
        CancellationToken cancellationToken)
    {
        try
        {
            // Update to Uploading
            job.Status = YoutubeUploadStatus.Uploading;
            await service.UpdateAsync(job, cancellationToken);

            // TODO: Implement actual YouTube upload logic here
            // var (videoId, videoUrl) = await UploadToYouTubeAsync(job);

            // On success
            job.Status = YoutubeUploadStatus.Uploaded;
            job.YoutubeVideoId = "dQw4w9WgXcQ"; // Replace with actual ID
            job.YoutubeUrl = $"https://www.youtube.com/watch?v={job.YoutubeVideoId}";
            await service.UpdateAsync(job, cancellationToken);

            _logger.LogInformation(
                "Successfully uploaded video {Title} - YouTube ID: {VideoId}",
                job.Title,
                job.YoutubeVideoId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload video {Title}", job.Title);

            job.Attempts++;
            job.LastError = ex.Message;

            if (job.Attempts >= job.MaxAttempts)
            {
                job.Status = YoutubeUploadStatus.Failed;
                _logger.LogWarning(
                    "Video {Title} failed permanently after {Attempts} attempts",
                    job.Title,
                    job.Attempts);
            }
            else
            {
                job.Status = YoutubeUploadStatus.Pending;
                _logger.LogInformation(
                    "Video {Title} will be retried. Attempt {Attempts}/{MaxAttempts}",
                    job.Title,
                    job.Attempts,
                    job.MaxAttempts);
            }

            await service.UpdateAsync(job, cancellationToken);
        }
    }
}
```

### Register Worker in Program.cs:

```csharp
builder.Services.AddHostedService<YoutubeUploadWorker>();
```
