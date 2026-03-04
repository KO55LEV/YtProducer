using System.Text.Json;
using Microsoft.Extensions.Configuration;
using YtProducer.Domain.Entities;
using YtProducer.Domain.Enums;

namespace YtProducer.Infrastructure.Services;

public sealed class MockPlaylistRepository : IPlaylistRepository
{
    private readonly List<Playlist> _playlists = new();
    private readonly string _jsonDataPath;

    public MockPlaylistRepository(IConfiguration configuration)
    {
        _jsonDataPath = configuration.GetValue<string>("MockData:JsonPath") 
            ?? Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "docs", "Playlist", "Outputs");
        
        LoadMockDataFromJsonFiles();
    }

    private void LoadMockDataFromJsonFiles()
    {
        if (!Directory.Exists(_jsonDataPath))
        {
            Console.WriteLine($"Mock data path not found: {_jsonDataPath}");
            return;
        }

        var jsonFiles = Directory.GetFiles(_jsonDataPath, "*.json");
        Console.WriteLine($"Loading {jsonFiles.Length} mock playlist files from {_jsonDataPath}");

        foreach (var file in jsonFiles)
        {
            try
            {
                var json = File.ReadAllText(file);
                var data = JsonSerializer.Deserialize<JsonPlaylistData>(json, new JsonSerializerOptions 
                { 
                    PropertyNameCaseInsensitive = true 
                });

                if (data == null) continue;

                var playlistId = Guid.NewGuid();
                var tracks = new List<Track>();

                if (data.Tracks != null)
                {
                    foreach (var trackData in data.Tracks)
                    {
                        var track = new Track
                        {
                            Id = Guid.NewGuid(),
                            PlaylistId = playlistId,
                            PlaylistPosition = trackData.PlaylistPosition,
                            Title = trackData.Title ?? "Untitled Track",
                            YouTubeTitle = trackData.YoutubeTitle,
                            Style = trackData.Style,
                            Duration = trackData.Duration,
                            TempoBpm = trackData.TempoBpm,
                            Key = trackData.Key,
                            EnergyLevel = trackData.EnergyLevel,
                            Status = TrackStatus.Ready,
                            CreatedAtUtc = DateTimeOffset.UtcNow.AddDays(-Random.Shared.Next(1, 30)),
                            UpdatedAtUtc = DateTimeOffset.UtcNow,
                            Metadata = JsonSerializer.Serialize(new
                            {
                                trackData.TitleViralityScore,
                                trackData.HookStrengthScore,
                                trackData.ThumbnailCtrScore,
                                trackData.HookType,
                                trackData.EnergyCurve,
                                trackData.ListeningScenario,
                                trackData.TargetAudience,
                                trackData.ThumbnailEmotion,
                                trackData.ThumbnailColorPalette,
                                trackData.PlaylistCategory,
                                trackData.Instruments,
                                trackData.StylePrompt,
                                trackData.ImagePrompt,
                                trackData.YoutubeDescription,
                                trackData.YoutubeTags
                            })
                        };
                        tracks.Add(track);
                    }
                }

                var playlist = new Playlist
                {
                    Id = playlistId,
                    Title = data.Theme ?? Path.GetFileNameWithoutExtension(file),
                    Theme = data.Theme,
                    Description = data.PlaylistStrategy,
                    PlaylistStrategy = data.PlaylistStrategy,
                    Status = PlaylistStatus.Active,
                    TrackCount = tracks.Count,
                    CreatedAtUtc = DateTimeOffset.UtcNow.AddDays(-Random.Shared.Next(1, 30)),
                    UpdatedAtUtc = DateTimeOffset.UtcNow,
                    PublishedAtUtc = DateTimeOffset.UtcNow.AddDays(-Random.Shared.Next(0, 15)),
                    Tracks = tracks
                };

                _playlists.Add(playlist);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to load {Path.GetFileName(file)}: {ex.Message}");
            }
        }

        Console.WriteLine($"Successfully loaded {_playlists.Count} mock playlists");
    }

    public Task<IReadOnlyList<Playlist>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<Playlist>>(_playlists.AsReadOnly());
    }

    public Task<Playlist?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var playlist = _playlists.FirstOrDefault(p => p.Id == id);
        return Task.FromResult(playlist);
    }

    public Task<Playlist> CreateAsync(Playlist playlist, CancellationToken cancellationToken = default)
    {
        playlist.Id = Guid.NewGuid();
        playlist.CreatedAtUtc = DateTimeOffset.UtcNow;
        playlist.UpdatedAtUtc = DateTimeOffset.UtcNow;
        
        foreach (var track in playlist.Tracks)
        {
            track.Id = Guid.NewGuid();
            track.PlaylistId = playlist.Id;
            track.CreatedAtUtc = DateTimeOffset.UtcNow;
            track.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }

        _playlists.Add(playlist);
        return Task.FromResult(playlist);
    }

    // JSON mapping classes
    private class JsonPlaylistData
    {
        public string? Theme { get; set; }
        public string? PlaylistStrategy { get; set; }
        public List<JsonTrackData>? Tracks { get; set; }
    }

    private class JsonTrackData
    {
        public int PlaylistPosition { get; set; }
        public string? Title { get; set; }
        public string? YoutubeTitle { get; set; }
        public int? TitleViralityScore { get; set; }
        public int? HookStrengthScore { get; set; }
        public int? ThumbnailCtrScore { get; set; }
        public string? Style { get; set; }
        public string? Duration { get; set; }
        public int? TempoBpm { get; set; }
        public string? Key { get; set; }
        public int? EnergyLevel { get; set; }
        public string? HookType { get; set; }
        public string? EnergyCurve { get; set; }
        public string? ListeningScenario { get; set; }
        public string? TargetAudience { get; set; }
        public string? ThumbnailEmotion { get; set; }
        public string? ThumbnailColorPalette { get; set; }
        public string? PlaylistCategory { get; set; }
        public List<string>? Instruments { get; set; }
        public string? StylePrompt { get; set; }
        public string? ImagePrompt { get; set; }
        public string? YoutubeDescription { get; set; }
        public List<string>? YoutubeTags { get; set; }
    }
}
