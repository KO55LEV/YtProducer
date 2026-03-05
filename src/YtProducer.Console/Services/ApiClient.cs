using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using YtProducer.Contracts.Playlists;
using YtProducer.Contracts.YoutubePlaylists;
using YtProducer.Contracts.YoutubeUploadQueue;

namespace YtProducer.Console.Services;

/// <summary>
/// HTTP client for communicating with YtProducer API endpoints.
/// Provides methods for all CRUD operations on Playlists, YouTube Playlists, and Upload Queue.
/// </summary>
public class ApiClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<ApiClient> _logger;
    private readonly string _baseUrl;

    // API endpoints
    private const string PlaylistsEndpoint = "/playlists";
    private const string YoutubePlaylistsEndpoint = "/youtube-playlists";
    private const string UploadQueueEndpoint = "/youtube-upload-queue";

    public ApiClient(HttpClient httpClient, ILogger<ApiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;

        // Get API base URL from environment or default
        _baseUrl = Environment.GetEnvironmentVariable("API_BASE_URL") ?? "http://localhost:8080/api";
        _httpClient.BaseAddress = new Uri(_baseUrl);

        _logger.LogInformation("✓ API Client initialized with base URL: {BaseUrl}", _baseUrl);
    }

    // ==================== PLAYLISTS ====================

    /// <summary>
    /// Get all playlists from the API.
    /// </summary>
    public async Task<List<PlaylistResponse>?> GetPlaylistsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("📡 GET {Endpoint}", PlaylistsEndpoint);
            var result = await _httpClient.GetFromJsonAsync<List<PlaylistResponse>>(PlaylistsEndpoint, cancellationToken);
            _logger.LogInformation("✓ Retrieved {Count} playlists", result?.Count ?? 0);
            return result;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "❌ Failed to get playlists: {Message}", ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Get a single playlist by ID.
    /// </summary>
    public async Task<PlaylistResponse?> GetPlaylistByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        try
        {
            var endpoint = $"{PlaylistsEndpoint}/{id}";
            _logger.LogInformation("📡 GET {Endpoint}", endpoint);
            var result = await _httpClient.GetFromJsonAsync<PlaylistResponse>(endpoint, cancellationToken);
            if (result != null)
                _logger.LogInformation("✓ Retrieved playlist: {Title}", result.Title);
            return result;
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogWarning("⚠️ Playlist not found: {Id}", id);
            return null;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "❌ Failed to get playlist {Id}: {Message}", id, ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Create a new playlist via the API.
    /// </summary>
    public async Task<PlaylistResponse?> CreatePlaylistAsync(
        CreatePlaylistRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("📡 POST {Endpoint} - Creating playlist: {Title}", PlaylistsEndpoint, request.Title);
            var response = await _httpClient.PostAsJsonAsync(PlaylistsEndpoint, request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<PlaylistResponse>(cancellationToken: cancellationToken);
            _logger.LogInformation("✓ Created playlist: {Id} - {Title}", result?.Id, result?.Title);
            return result;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "❌ Failed to create playlist: {Message}", ex.Message);
            return null;
        }
    }

    // ==================== YOUTUBE PLAYLISTS ====================

    /// <summary>
    /// Get all YouTube playlists from the API.
    /// </summary>
    public async Task<List<YoutubePlaylistResponse>?> GetYoutubePlaylistsAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("📡 GET {Endpoint}", YoutubePlaylistsEndpoint);
            var result = await _httpClient.GetFromJsonAsync<List<YoutubePlaylistResponse>>(YoutubePlaylistsEndpoint, cancellationToken);
            _logger.LogInformation("✓ Retrieved {Count} YouTube playlists", result?.Count ?? 0);
            return result;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "❌ Failed to get YouTube playlists: {Message}", ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Get a single YouTube playlist by ID.
    /// </summary>
    public async Task<YoutubePlaylistResponse?> GetYoutubePlaylistByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var endpoint = $"{YoutubePlaylistsEndpoint}/{id}";
            _logger.LogInformation("📡 GET {Endpoint}", endpoint);
            var result = await _httpClient.GetFromJsonAsync<YoutubePlaylistResponse>(endpoint, cancellationToken);
            if (result != null)
                _logger.LogInformation("✓ Retrieved YouTube playlist: {Title}", result.Title);
            return result;
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogWarning("⚠️ YouTube playlist not found: {Id}", id);
            return null;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "❌ Failed to get YouTube playlist {Id}: {Message}", id, ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Create a new YouTube playlist via the API.
    /// </summary>
    public async Task<YoutubePlaylistResponse?> CreateYoutubePlaylistAsync(
        CreateYoutubePlaylistRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("📡 POST {Endpoint} - Creating YouTube playlist: {Title}", 
                YoutubePlaylistsEndpoint, request.Title);
            var response = await _httpClient.PostAsJsonAsync(YoutubePlaylistsEndpoint, request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<YoutubePlaylistResponse>(cancellationToken: cancellationToken);
            _logger.LogInformation("✓ Created YouTube playlist: {Id} - {Title}", result?.Id, result?.Title);
            return result;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "❌ Failed to create YouTube playlist: {Message}", ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Update an existing YouTube playlist via the API.
    /// </summary>
    public async Task<YoutubePlaylistResponse?> UpdateYoutubePlaylistAsync(
        Guid id,
        UpdateYoutubePlaylistRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var endpoint = $"{YoutubePlaylistsEndpoint}/{id}";
            _logger.LogInformation("📡 PUT {Endpoint} - Updating YouTube playlist", endpoint);
            var response = await _httpClient.PutAsJsonAsync(endpoint, request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<YoutubePlaylistResponse>(cancellationToken: cancellationToken);
            _logger.LogInformation("✓ Updated YouTube playlist: {Id} - {Title}", result?.Id, result?.Title);
            return result;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "❌ Failed to update YouTube playlist {Id}: {Message}", id, ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Delete a YouTube playlist via the API.
    /// </summary>
    public async Task<bool> DeleteYoutubePlaylistAsync(Guid id, CancellationToken cancellationToken = default)
    {
        try
        {
            var endpoint = $"{YoutubePlaylistsEndpoint}/{id}";
            _logger.LogInformation("📡 DELETE {Endpoint}", endpoint);
            var response = await _httpClient.DeleteAsync(endpoint, cancellationToken);
            response.EnsureSuccessStatusCode();

            _logger.LogInformation("✓ Deleted YouTube playlist: {Id}", id);
            return true;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "❌ Failed to delete YouTube playlist {Id}: {Message}", id, ex.Message);
            return false;
        }
    }

    // ==================== UPLOAD QUEUE ====================

    /// <summary>
    /// Get all upload queue items from the API.
    /// </summary>
    public async Task<List<YoutubeUploadQueueResponse>?> GetUploadQueueAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("📡 GET {Endpoint}", UploadQueueEndpoint);
            var result = await _httpClient.GetFromJsonAsync<List<YoutubeUploadQueueResponse>>(UploadQueueEndpoint, cancellationToken);
            _logger.LogInformation("✓ Retrieved {Count} upload queue items", result?.Count ?? 0);
            return result;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "❌ Failed to get upload queue: {Message}", ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Get a single upload queue item by ID.
    /// </summary>
    public async Task<YoutubeUploadQueueResponse?> GetUploadQueueItemAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var endpoint = $"{UploadQueueEndpoint}/{id}";
            _logger.LogInformation("📡 GET {Endpoint}", endpoint);
            var result = await _httpClient.GetFromJsonAsync<YoutubeUploadQueueResponse>(endpoint, cancellationToken);
            if (result != null)
                _logger.LogInformation("✓ Retrieved upload queue item: {Title}", result.Title);
            return result;
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogWarning("⚠️ Upload queue item not found: {Id}", id);
            return null;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "❌ Failed to get upload queue item {Id}: {Message}", id, ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Create a new upload queue item via the API.
    /// </summary>
    public async Task<YoutubeUploadQueueResponse?> CreateUploadQueueItemAsync(
        CreateYoutubeUploadQueueRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("📡 POST {Endpoint} - Creating upload queue item: {Title}", 
                UploadQueueEndpoint, request.Title);
            var response = await _httpClient.PostAsJsonAsync(UploadQueueEndpoint, request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<YoutubeUploadQueueResponse>(cancellationToken: cancellationToken);
            _logger.LogInformation("✓ Created upload queue item: {Id} - {Title}", result?.Id, result?.Title);
            return result;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "❌ Failed to create upload queue item: {Message}", ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Update an existing upload queue item via the API.
    /// </summary>
    public async Task<YoutubeUploadQueueResponse?> UpdateUploadQueueItemAsync(
        Guid id,
        UpdateYoutubeUploadQueueRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var endpoint = $"{UploadQueueEndpoint}/{id}";
            _logger.LogInformation("📡 PUT {Endpoint} - Updating upload queue item", endpoint);
            var response = await _httpClient.PutAsJsonAsync(endpoint, request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<YoutubeUploadQueueResponse>(cancellationToken: cancellationToken);
            _logger.LogInformation("✓ Updated upload queue item: {Id} - {Title}", result?.Id, result?.Title);
            return result;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "❌ Failed to update upload queue item {Id}: {Message}", id, ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Delete an upload queue item via the API.
    /// </summary>
    public async Task<bool> DeleteUploadQueueItemAsync(Guid id, CancellationToken cancellationToken = default)
    {
        try
        {
            var endpoint = $"{UploadQueueEndpoint}/{id}";
            _logger.LogInformation("📡 DELETE {Endpoint}", endpoint);
            var response = await _httpClient.DeleteAsync(endpoint, cancellationToken);
            response.EnsureSuccessStatusCode();

            _logger.LogInformation("✓ Deleted upload queue item: {Id}", id);
            return true;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "❌ Failed to delete upload queue item {Id}: {Message}", id, ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Get the next pending upload queue item (worker endpoint).
    /// </summary>
    public async Task<YoutubeUploadQueueResponse?> GetNextPendingUploadAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var endpoint = $"{UploadQueueEndpoint}/next";
            _logger.LogInformation("📡 GET {Endpoint} - Getting next pending upload", endpoint);
            var response = await _httpClient.GetAsync(endpoint, cancellationToken);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogWarning("⚠️ No pending uploads in queue");
                return null;
            }

            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<YoutubeUploadQueueResponse>(cancellationToken: cancellationToken);
            _logger.LogInformation("✓ Retrieved next pending upload: {Title}", result?.Title);
            return result;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "❌ Failed to get next pending upload: {Message}", ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Check if API is available and running.
    /// </summary>
    public async Task<bool> HealthCheckAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("🏥 Checking API health...");
            var response = await _httpClient.GetAsync("/health", cancellationToken);
            var isHealthy = response.IsSuccessStatusCode;

            if (isHealthy)
            {
                _logger.LogInformation("✓ API is healthy and running");
            }
            else
            {
                _logger.LogWarning("⚠️ API health check failed with status {StatusCode}", response.StatusCode);
            }

            return isHealthy;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "❌ Failed to reach API: {Message}", ex.Message);
            _logger.LogError("   Ensure API is running at {BaseUrl}", _baseUrl);
            return false;
        }
    }
}
