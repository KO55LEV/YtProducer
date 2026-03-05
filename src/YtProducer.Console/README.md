# YtProducer Console Application

A command-line utility for rapid testing and API integration testing of the YtProducer backend.

## Purpose

- **API Testing**: Test all REST API endpoints without running the full web UI
- **Quick Iteration**: Test business logic changes rapidly during development
- **Database Inspection**: Query and verify data in the database
- **Integration Testing**: Combine database operations with API calls

## Features

### ApiClient Service
HTTP client providing methods for:
- **Playlists CRUD** - Create, read, update (via API)
- **YouTube Playlists CRUD** - Full CRUD operations via API
- **Upload Queue CRUD** - Full CRUD + worker "GetNextPending" endpoint
- **Health Check** - Verify API is running

### YtService 
Orchestrates database and API operations:
- Loads existing database records
- Executes 6 test scenarios covering all APIs
- Logs detailed output for debugging
- Tests CRUD operations and relationships

## Prerequisites

### Required Services

1. **PostgreSQL** on `localhost:5432`
   ```powershell
   docker-compose up -d postgres
   ```

2. **API Server** on `localhost:8080`
   ```powershell
   cd src/YtProducer.Api
   dotnet run
   # Wait for: "Now listening on: http://localhost:8080"
   ```

3. **Database & Config** in `.env`:
   ```
   POSTGRES_HOST=localhost
   POSTGRES_PORT=5432
   POSTGRES_DATABASE=ytproducer
   POSTGRES_USER=ytproducer
   POSTGRES_PASSWORD=ytproducer
   API_BASE_URL=http://localhost:8080/api
   USE_MOCK_DATA=false
   ```

## Running Console App

### Quick Start (PowerShell):
```powershell
.\run-console.ps1
```

### Manual:
```powershell
cd src/YtProducer.Console
dotnet run
```

## Working Service Output

```
✓ Environment variables loaded from .env file

╔════════════════════════════════════════════════════╗
║       YtProducer Working Service - API Testing     ║
╚════════════════════════════════════════════════════╝

🏥 Checking API health...
✓ API is healthy and running

📊 STEP 1: Loading Existing Data from Database
═══════════════════════════════════════════════════

  🎵 Playlists:            3
  ▶️ YouTube Playlists:     2
  📤 Upload Queue Items:    5
  ⚙️ Jobs:                  4

  Recent Playlists:
    • Workout Mix (Active)
    • Gym Energy (Draft)
    • Running Tunes (Active)

🌐 STEP 2: Testing API Calls
════════════════════════════

TEST 1️⃣ : Get All YouTube Playlists
─────────────────────────────────────
📡 GET /youtube-playlists
✓ Retrieved 2 YouTube playlists from API
  • My Workouts (ID: PLxxxxxx_a1b2c3d4)
  • Training Music (ID: PLyyyyyy_e5f6g7h8)

TEST 2️⃣ : Create YouTube Playlist via API
──────────────────────────────────────────
📡 POST /youtube-playlists - Creating YouTube playlist: API Test Playlist
✓ Created playlist: 12345678-1234-1234-1234-123456789012 - API Test Playlist
  ID: 12345678-1234-1234-1234-123456789012
  YouTube ID: PLtest_a1b2c3d4

  → Updating playlist...
📡 PUT /youtube-playlists/{id} - Updating YouTube playlist
✓ Updated YouTube playlist: 12345678-1234-1234-1234-123456789012 - API Test Playlist (Updated)
  ✓ Updated: API Test Playlist (Updated)

TEST 3️⃣ : Get Upload Queue Items
──────────────────────────────────
📡 GET /youtube-upload-queue
✓ Retrieved 5 items from upload queue
  • Morning Workout (Pending) - Priority 1
  • Evening Session (Pending) - Priority 2
  • Quick Cardio (Uploading) - Priority 3

TEST 4️⃣ : Create Upload Queue Item via API
───────────────────────────────────────────
📡 POST /youtube-upload-queue - Creating upload queue item: API Test Video Upload
✓ Created queue item: 87654321-4321-4321-4321-210987654321 - API Test Video Upload
  ID: 87654321-4321-4321-4321-210987654321
  Status: Pending
  Scheduled: 2026-03-05 17:30:00 +00:00

  → Updating queue item...
📡 PUT /youtube-upload-queue/{id} - Updating upload queue item
✓ Updated upload queue item: 87654321-4321-4321-4321-210987654321 - API Test Video Upload (Updated)
  ✓ Updated: Priority 2

TEST 5️⃣ : Get Next Pending Upload (Worker Query)
──────────────────────────────────────────────────
📡 GET /youtube-upload-queue/next - Getting next pending upload
✓ Retrieved next pending upload: Morning Workout
✓ Next pending upload:
  Title: Morning Workout
  Priority: 1
  Status: Pending

TEST 6️⃣ : Create Playlist with Tracks via API
──────────────────────────────────────────────
📡 POST /playlists - Creating playlist: API Test Playlist
✓ Created playlist: 11111111-2222-3333-4444-555555555555 - API Test Playlist
  ID: 11111111-2222-3333-4444-555555555555
  Track Count: 2
  Tracks:
    • Test Track 1
    • Test Track 2

✓ Working service completed!
```

## Using ApiClient in Code

The ApiClient is registered in DI and can be injected:

```csharp
public class MyService
{
    private readonly ApiClient _apiClient;
    
    public MyService(ApiClient apiClient)
    {
        _apiClient = apiClient;
    }
    
    public async Task TestAsync()
    {
        // Get all playlists
        var playlists = await _apiClient.GetPlaylistsAsync();
        
        // Create YouTube playlist
        var request = new CreateYoutubePlaylistRequest(
            YoutubePlaylistId: "PLxxxxxx",
            Title: "My Playlist",
            Description: "Test",
            Status: "Draft",
            // ... other params
        );
        var created = await _apiClient.CreateYoutubePlaylistAsync(request);
        
        // Get next pending upload (worker pattern)
        var nextUpload = await _apiClient.GetNextPendingUploadAsync();
    }
}
```

## Extending YtService

Add custom test scenarios to [Services/YtService.cs](Services/YtService.cs):

```csharp
private async Task TestCustomAsync()
{
    _logger.LogInformation("TEST 7️⃣: Custom Scenario");
    _logger.LogInformation("────────────────────────");
    
    // Combine database and API operations
    var dbPlaylists = await _context.Playlists.ToListAsync();
    var apiPlaylists = await _apiClient.GetPlaylistsAsync();
    
    _logger.LogInformation("Database has {Count}, API has {Count}", 
        dbPlaylists.Count, 
        apiPlaylists?.Count ?? 0);
}
```

Then call from `RunPlaylistInitAsync()`:
```csharp
await TestCustomAsync();
```

Recompile with `dotnet build` to verify additions.

## API Endpoints

All API endpoints available via ApiClient:

| Method | Endpoint | ApiClient Method |
|--------|----------|------------------|
| GET | `/playlists` | `GetPlaylistsAsync()` |
| GET | `/playlists/{id}` | `GetPlaylistByIdAsync(id)` |
| POST | `/playlists` | `CreatePlaylistAsync(request)` |
| GET | `/youtube-playlists` | `GetYoutubePlaylistsAsync()` |
| GET | `/youtube-playlists/{id}` | `GetYoutubePlaylistByIdAsync(id)` |
| POST | `/youtube-playlists` | `CreateYoutubePlaylistAsync(request)` |
| PUT | `/youtube-playlists/{id}` | `UpdateYoutubePlaylistAsync(id, request)` |
| DELETE | `/youtube-playlists/{id}` | `DeleteYoutubePlaylistAsync(id)` |
| GET | `/youtube-upload-queue` | `GetUploadQueueAsync()` |
| GET | `/youtube-upload-queue/{id}` | `GetUploadQueueItemAsync(id)` |
| POST | `/youtube-upload-queue` | `CreateUploadQueueItemAsync(request)` |
| PUT | `/youtube-upload-queue/{id}` | `UpdateUploadQueueItemAsync(id, request)` |
| DELETE | `/youtube-upload-queue/{id}` | `DeleteUploadQueueItemAsync(id)` |
| GET | `/youtube-upload-queue/next` | `GetNextPendingUploadAsync()` |
| GET | `/health` | `HealthCheckAsync()` |

## Troubleshooting

### API Not Responding
```
❌ Failed to reach API: Connection refused
   Ensure API is running at http://localhost:8080/api
```

Solution:
```powershell
cd src/YtProducer.Api
dotnet run
```

### Database Connection Failed
```
PostgreSQL connection failed
```

Solution:
1. Check `.env` credentials
2. Verify: `docker-compose ps postgres`
3. Reconnect: `docker-compose down && docker-compose up -d postgres`

### Schema Not Found
```
relation "playlists" does not exist
```

Solution:
```bash
docker exec ytproducer-postgres psql -U ytproducer -d ytproducer -f /docker-entrypoint-initdb.d/init.sql
```

### Connection Timeouts
- Increase timeout in ApiClient constructor
- Check API is listening: `curl http://localhost:8080/health`
- Check firewall settings

## Configuration

Edit `.env` to customize:

```env
# Database
POSTGRES_HOST=localhost
POSTGRES_PORT=5432
POSTGRES_DATABASE=ytproducer
POSTGRES_USER=ytproducer
POSTGRES_PASSWORD=ytproducer

# API
API_BASE_URL=http://localhost:8080/api

# Database mode
USE_MOCK_DATA=false

# Logging
LOG_LEVEL=Information
```

## Next Steps

1. Start the API server
2. Run console app with `.\run-console.ps1`
3. Review output for database and API health
4. Add custom test scenarios
5. Use ApiClient for integration testing

## Architecture

```
Program.cs
├── Loads .env configuration
├── Configures DI (DbContext, ApiClient, YtService)
└── Runs YtService

YtService
├── LoadExistingDataAsync()
│   └── Queries YtProducerDbContext
└── TestApiCallsAsync()
    └── Calls ApiClient methods

ApiClient
├── GetFromJsonAsync<T>()
├── PostAsJsonAsync<T>()
├── PutAsJsonAsync<T>()
└── DeleteAsync()
```

## Dependencies

- Microsoft.EntityFrameworkCore
- Microsoft.Extensions.Http
- Microsoft.Extensions.Logging
- YtProducer.Infrastructure
- YtProducer.Contracts
- System.Net.Http.Json (for API calls)
