# YouTube Upload Queue API

Complete implementation of the YouTube upload queue feature.

## Overview

This feature provides a queue system for managing video uploads to YouTube with the following capabilities:

- Priority-based queue management
- Scheduled uploads
- Automatic retry logic
- Status tracking
- Full CRUD operations via REST API

## Database Schema

**Table:** `youtube_upload_queue`

| Column | Type | Default | Description |
|--------|------|---------|-------------|
| id | uuid | - | Primary key |
| status | varchar(32) | 'Pending' | Upload status |
| priority | integer | 0 | Queue priority |
| title | varchar(255) | - | Video title |
| description | varchar(5000) | null | Video description |
| tags | text[] | null | Video tags |
| category_id | integer | 10 | YouTube category |
| video_file_path | varchar(1000) | - | Path to video file |
| thumbnail_file_path | varchar(1000) | null | Path to thumbnail |
| youtube_video_id | varchar(128) | null | YouTube video ID |
| youtube_url | varchar(500) | null | YouTube video URL |
| scheduled_upload_at | timestamptz | null | Scheduled upload time |
| attempts | integer | 0 | Upload attempts |
| max_attempts | integer | 5 | Maximum retry attempts |
| last_error | text | null | Last error message |
| created_at | timestamptz | NOW() | Creation timestamp |
| updated_at | timestamptz | NOW() | Update timestamp |

**Indexes:**
- `ix_youtube_upload_queue_status`
- `ix_youtube_upload_queue_scheduled_upload_at`
- `ix_youtube_upload_queue_priority`
- `ix_youtube_upload_queue_composite` (status, scheduled_upload_at, priority)

## Enums

### YoutubeUploadStatus

```csharp
public enum YoutubeUploadStatus
{
    Pending = 0,
    Uploading = 1,
    Uploaded = 2,
    Failed = 3
}
```

## API Endpoints

### Create Upload Queue Item

```http
POST /youtube-upload-queue
Content-Type: application/json

{
  "title": "My Video Title",
  "description": "Video description",
  "tags": ["tag1", "tag2"],
  "categoryId": 10,
  "videoFilePath": "/path/to/video.mp4",
  "thumbnailFilePath": "/path/to/thumbnail.jpg",
  "priority": 5,
  "scheduledUploadAt": "2026-03-06T10:00:00Z",
  "maxAttempts": 3
}
```

**Response:** `201 Created`

### Get All Queue Items

```http
GET /youtube-upload-queue
```

**Response:** `200 OK` - Array of queue items

### Get Queue Item by ID

```http
GET /youtube-upload-queue/{id}
```

**Response:** `200 OK` or `404 Not Found`

### Update Queue Item

```http
PUT /youtube-upload-queue/{id}
Content-Type: application/json

{
  "status": "Uploaded",
  "youtubeVideoId": "dQw4w9WgXcQ",
  "youtubeUrl": "https://www.youtube.com/watch?v=dQw4w9WgXcQ"
}
```

**Response:** `200 OK` or `404 Not Found`

### Delete Queue Item

```http
DELETE /youtube-upload-queue/{id}
```

**Response:** `204 No Content` or `404 Not Found`

### Get Next Pending Upload (Worker Endpoint)

```http
GET /youtube-upload-queue/next
```

Returns the next video ready to be uploaded based on:
- Status = Pending
- ScheduledUploadAt <= now (or null)
- Ordered by Priority DESC, CreatedAt ASC

**Response:** `200 OK` or `404 Not Found`

## Service Layer

### IYoutubeUploadQueueService

```csharp
public interface IYoutubeUploadQueueService
{
    Task<YoutubeUploadQueue> CreateAsync(YoutubeUploadQueue queue, CancellationToken cancellationToken = default);
    Task<YoutubeUploadQueue?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<YoutubeUploadQueue>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<YoutubeUploadQueue> UpdateAsync(YoutubeUploadQueue queue, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task<YoutubeUploadQueue?> GetNextPendingAsync(CancellationToken cancellationToken = default);
}
```

## Files Created

### Domain Layer
- `src/YtProducer.Domain/Enums/YoutubeUploadStatus.cs`
- `src/YtProducer.Domain/Entities/YoutubeUploadQueue.cs`

### Infrastructure Layer
- `src/YtProducer.Infrastructure/Persistence/Configurations/YoutubeUploadQueueConfiguration.cs`
- `src/YtProducer.Infrastructure/Services/IYoutubeUploadQueueService.cs`
- `src/YtProducer.Infrastructure/Services/YoutubeUploadQueueService.cs`

### API Layer
- `src/YtProducer.Api/Endpoints/YoutubeUploadQueueEndpoints.cs`

### Contracts Layer
- `src/YtProducer.Contracts/YoutubeUploadQueue/CreateYoutubeUploadQueueRequest.cs`
- `src/YtProducer.Contracts/YoutubeUploadQueue/UpdateYoutubeUploadQueueRequest.cs`
- `src/YtProducer.Contracts/YoutubeUploadQueue/YoutubeUploadQueueResponse.cs`

### Database
- `docker/postgres/init.sql` - Updated with table creation

### Configuration
- `src/YtProducer.Infrastructure/Persistence/YtProducerDbContext.cs` - Added DbSet
- `src/YtProducer.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs` - Registered service
- `src/YtProducer.Api/Program.cs` - Registered endpoints

## Usage Example

### Creating a Queue Item

```bash
curl -X POST http://localhost:8080/youtube-upload-queue \
  -H "Content-Type: application/json" \
  -d '{
    "title": "My Awesome Gym Workout Video",
    "description": "High-energy workout music",
    "tags": ["gym", "workout", "fitness"],
    "categoryId": 10,
    "videoFilePath": "/media/videos/workout-001.mp4",
    "thumbnailFilePath": "/media/thumbnails/workout-001.jpg",
    "priority": 5,
    "scheduledUploadAt": "2026-03-06T10:00:00Z",
    "maxAttempts": 3
  }'
```

### Fetching Next Job (Worker)

```bash
curl -X GET http://localhost:8080/youtube-upload-queue/next
```

### Updating Status After Upload

```bash
curl -X PUT http://localhost:8080/youtube-upload-queue/{id} \
  -H "Content-Type: application/json" \
  -d '{
    "status": "Uploaded",
    "youtubeVideoId": "dQw4w9WgXcQ",
    "youtubeUrl": "https://www.youtube.com/watch?v=dQw4w9WgXcQ"
  }'
```

## Worker Implementation

See `youtube-upload-queue-worker.md` for complete worker implementation example.

## Production Considerations

1. **File Storage**: Ensure video and thumbnail file paths are accessible to the worker
2. **YouTube API**: Implement proper YouTube Data API v3 authentication and upload logic
3. **Error Handling**: Monitor failed uploads and implement alerting
4. **Quota Management**: YouTube API has daily quotas - implement rate limiting
5. **Concurrency**: If running multiple workers, implement distributed locking
6. **File Cleanup**: Clean up uploaded files after successful upload
7. **Monitoring**: Add metrics for upload success rate, queue depth, and processing time
