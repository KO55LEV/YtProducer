using System.Text.Json;
using System.Text.Json.Serialization;
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
                            Style = trackData.StyleSummary ?? trackData.Style,
                            Duration = trackData.DurationSeconds?.ToString() ?? trackData.Duration,
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
                                trackData.SongStructure,
                                trackData.EnergyCurve,
                                trackData.ListeningScenario,
                                trackData.TargetAudience,
                                trackData.ThumbnailEmotion,
                                trackData.ThumbnailColorPalette,
                                trackData.ThumbnailTextHint,
                                trackData.PlaylistCategory,
                                trackData.Instruments,
                                trackData.VisualStyleHint,
                                trackData.StylePrompt,
                                trackData.MusicGenerationPrompt,
                                trackData.Lyrics,
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
                    Title = data.PlaylistTitle ?? data.Theme ?? Path.GetFileNameWithoutExtension(file),
                    Theme = data.Theme,
                    Description = data.PlaylistDescription ?? data.PlaylistStrategy,
                    PlaylistStrategy = data.PlaylistStrategy,
                    Metadata = JsonSerializer.Serialize(new
                    {
                        data.TargetPlatform,
                        data.PlaylistTitle,
                        data.PlaylistDescription
                    }),
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
        [JsonPropertyName("theme")]
        public string? Theme { get; set; }

        [JsonPropertyName("playlist_title")]
        public string? PlaylistTitle { get; set; }

        [JsonPropertyName("playlist_description")]
        public string? PlaylistDescription { get; set; }

        [JsonPropertyName("playlist_strategy")]
        public string? PlaylistStrategy { get; set; }

        [JsonPropertyName("target_platform")]
        public string? TargetPlatform { get; set; }

        [JsonPropertyName("tracks")]
        public List<JsonTrackData>? Tracks { get; set; }
    }

    private class JsonTrackData
    {
        [JsonPropertyName("playlist_position")]
        public int PlaylistPosition { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("youtube_title")]
        public string? YoutubeTitle { get; set; }

        [JsonPropertyName("title_virality_score")]
        public int? TitleViralityScore { get; set; }

        [JsonPropertyName("hook_strength_score")]
        public int? HookStrengthScore { get; set; }

        [JsonPropertyName("thumbnail_ctr_score")]
        public int? ThumbnailCtrScore { get; set; }

        [JsonPropertyName("style_summary")]
        public string? StyleSummary { get; set; }

        [JsonPropertyName("style")]
        public string? Style { get; set; }

        [JsonPropertyName("duration")]
        public string? Duration { get; set; }

        [JsonPropertyName("duration_seconds")]
        public int? DurationSeconds { get; set; }

        [JsonPropertyName("tempo_bpm")]
        public int? TempoBpm { get; set; }

        [JsonPropertyName("key")]
        public string? Key { get; set; }

        [JsonPropertyName("energy_level")]
        public int? EnergyLevel { get; set; }

        [JsonPropertyName("hook_type")]
        public string? HookType { get; set; }

        [JsonPropertyName("song_structure")]
        public string? SongStructure { get; set; }

        [JsonPropertyName("energy_curve")]
        public string? EnergyCurve { get; set; }

        [JsonPropertyName("listening_scenario")]
        public string? ListeningScenario { get; set; }

        [JsonPropertyName("target_audience")]
        public string? TargetAudience { get; set; }

        [JsonPropertyName("thumbnail_emotion")]
        public string? ThumbnailEmotion { get; set; }

        [JsonPropertyName("thumbnail_color_palette")]
        public string? ThumbnailColorPalette { get; set; }

        [JsonPropertyName("thumbnail_text_hint")]
        public string? ThumbnailTextHint { get; set; }

        [JsonPropertyName("playlist_category")]
        public string? PlaylistCategory { get; set; }

        [JsonPropertyName("visual_style_hint")]
        public string? VisualStyleHint { get; set; }

        [JsonPropertyName("instruments")]
        public List<string>? Instruments { get; set; }

        [JsonPropertyName("style_prompt")]
        public string? StylePrompt { get; set; }

        [JsonPropertyName("lyrics")]
        public string? Lyrics { get; set; }

        [JsonPropertyName("music_generation_prompt")]
        public string? MusicGenerationPrompt { get; set; }

        [JsonPropertyName("image_prompt")]
        public string? ImagePrompt { get; set; }

        [JsonPropertyName("youtube_description")]
        public string? YoutubeDescription { get; set; }

        [JsonPropertyName("youtube_tags")]
        public List<string>? YoutubeTags { get; set; }
    }
}
