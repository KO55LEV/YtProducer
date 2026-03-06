using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Diagnostics;
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
    private static readonly string[] ImageExtensions = [".jpg", ".jpeg", ".png", ".webp"];
    private static readonly string[] AudioExtensions = [".mp3"];
    private static readonly int[] YoutubePublishHoursUtc = [8, 14, 18];
    private const string MediaGenerationStateFileName = ".media-generation-state.json";

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

    public async Task RunPlaylistInitAsync()
    {
        _logger.LogInformation("╔════════════════════════════════════════════════════╗");
        _logger.LogInformation("║       YtProducer Console - Playlist Pipeline        ║");
        _logger.LogInformation("╚════════════════════════════════════════════════════╝\n");

        await PrintAllPlaylistsAsync();
        _logger.LogInformation("");
        await PrintAllYoutubePlaylistsAsync();
        _logger.LogInformation("");
        await RunPlaylistPipelineAsync();

        _logger.LogInformation("\n✓ Playlist pipeline completed!");
    }

    public async Task PrintPlaylistListAsync()
    {
        var playlists = await _context.Playlists
            .AsNoTracking()
            .OrderByDescending(p => p.CreatedAtUtc)
            .Select(p => new { p.Id, p.Title, p.CreatedAtUtc })
            .ToListAsync();

        var workingDirectory = Environment.GetEnvironmentVariable("YT_PRODUCER_PLAYLIST_WORKING_DIRECTORY");

        var rows = playlists
            .Select(playlist =>
            {
                var folderName = GetPlaylistFolderName(playlist.Id);
                var folderExists = !string.IsNullOrWhiteSpace(workingDirectory)
                    && Directory.Exists(Path.Combine(workingDirectory, folderName));

                return new
                {
                    playlist.Id,
                    playlist.Title,
                    playlist.CreatedAtUtc,
                    IsFolderExists = folderExists
                };
            })
            .OrderBy(row => row.IsFolderExists)
            .ThenByDescending(row => row.CreatedAtUtc)
            .ToList();

        global::System.Console.WriteLine("playlist_id\ttitle\tis_folder_exists");
        foreach (var row in rows)
        {
            var title = string.IsNullOrWhiteSpace(row.Title) ? "-" : row.Title.Trim();
            global::System.Console.WriteLine($"{row.Id}\t{title}\t{row.IsFolderExists.ToString().ToLowerInvariant()}");
        }
    }

    public async Task RunGenerateMediaAsync()
    {
        var workingDirectory = Environment.GetEnvironmentVariable("YT_PRODUCER_PLAYLIST_WORKING_DIRECTORY");
        if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            global::System.Console.WriteLine("YT_PRODUCER_PLAYLIST_WORKING_DIRECTORY is not configured.");
            return;
        }

        var mcpMediaWorkingDirectory = Environment.GetEnvironmentVariable("YT_PRODUCER_MCP_MEDIA_WORKING_DIRECTORY");
        if (string.IsNullOrWhiteSpace(mcpMediaWorkingDirectory))
        {
            global::System.Console.WriteLine("YT_PRODUCER_MCP_MEDIA_WORKING_DIRECTORY is not configured.");
            return;
        }

        var mcpMediaProject = Environment.GetEnvironmentVariable("YT_PRODUCER_MCP_MEDIA_PROJECT")
            ?? "OnlineTeamTools.MCP.Media/OnlineTeamTools.MCP.Media.csproj";
        var staleMinutes = ResolveStaleInProgressMinutes();
        var parallelism = ResolveMediaParallelism();
        using var renderSemaphore = new SemaphoreSlim(parallelism, parallelism);

        if (!Directory.Exists(mcpMediaWorkingDirectory))
        {
            global::System.Console.WriteLine($"MCP media working directory does not exist: {mcpMediaWorkingDirectory}");
            return;
        }

        var playlists = await _context.Playlists
            .AsNoTracking()
            .OrderByDescending(p => p.CreatedAtUtc)
            .Select(p => new { p.Id, p.Title })
            .ToListAsync();

        var summary = new MediaGenerationSummary();
        foreach (var playlist in playlists)
        {
            var playlistFolderPath = Path.Combine(workingDirectory, GetPlaylistFolderName(playlist.Id));
            if (!Directory.Exists(playlistFolderPath))
            {
                continue;
            }

            summary.PlaylistsScanned++;
            var playlistTitle = string.IsNullOrWhiteSpace(playlist.Title) ? "-" : playlist.Title.Trim();
            CleanupStalePlaylistLock(playlistFolderPath, TimeSpan.FromMinutes(staleMinutes));
            var playlistOutputDir = playlistFolderPath;
            Directory.CreateDirectory(playlistOutputDir);
            using var playlistLock = TryAcquirePlaylistLock(playlistFolderPath);
            if (playlistLock == null)
            {
                summary.PlaylistsLocked++;
                global::System.Console.WriteLine($"skip locked playlist {playlist.Id} ({playlistTitle})");
                continue;
            }

            var state = LoadMediaGenerationState(playlistFolderPath);
            ExpireStaleInProgressEntries(state, TimeSpan.FromMinutes(staleMinutes));

            var candidates = DiscoverRenderCandidates(playlistFolderPath);
            if (candidates.Count == 0)
            {
                SaveMediaGenerationState(playlistFolderPath, state);
                continue;
            }

            var playlistStateLock = new SemaphoreSlim(1, 1);
            var renderTasks = new List<Task>();

            foreach (var candidate in candidates)
            {
                summary.CandidatesFound++;
                renderTasks.Add(Task.Run(async () =>
                {
                    await renderSemaphore.WaitAsync();
                    try
                    {
                        await playlistStateLock.WaitAsync();
                        try
                        {
                            var latestState = LoadMediaGenerationState(playlistFolderPath);
                            ExpireStaleInProgressEntries(latestState, TimeSpan.FromMinutes(staleMinutes));

                            if (candidate.LocalVideoPath != null && File.Exists(candidate.LocalVideoPath))
                            {
                                MarkCompletedFromLocalVideo(latestState, candidate);
                                SaveMediaGenerationState(playlistFolderPath, latestState);
                                summary.SkippedCompleted++;
                                return;
                            }

                            if (latestState.Entries.TryGetValue(candidate.Key, out var existing))
                            {
                                if (string.Equals(existing.Status, "in_progress", StringComparison.OrdinalIgnoreCase))
                                {
                                    summary.SkippedInProgress++;
                                    return;
                                }

                                if (string.Equals(existing.Status, "completed", StringComparison.OrdinalIgnoreCase))
                                {
                                    summary.SkippedCompleted++;
                                    return;
                                }
                            }

                            var entry = latestState.Entries.TryGetValue(candidate.Key, out var entryValue)
                                ? entryValue
                                : new MediaGenerationEntry();
                            entry.Status = "in_progress";
                            entry.AudioPath = candidate.AudioPath;
                            entry.ImagePath = candidate.ImagePath;
                            entry.LastStartedAtUtc = DateTimeOffset.UtcNow;
                            entry.Attempts++;
                            entry.LastError = null;
                            latestState.Entries[candidate.Key] = entry;
                            SaveMediaGenerationState(playlistFolderPath, latestState);
                        }
                        finally
                        {
                            playlistStateLock.Release();
                        }

                        global::System.Console.WriteLine($"render start playlist={playlist.Id} key={candidate.Key}");
                        var result = await ExecuteVisualizerRenderAsync(
                            mcpMediaWorkingDirectory,
                            mcpMediaProject,
                            mcpMediaWorkingDirectory,
                            playlistOutputDir,
                            candidate.ImagePath,
                            candidate.AudioPath);

                        await playlistStateLock.WaitAsync();
                        try
                        {
                            var latestState = LoadMediaGenerationState(playlistFolderPath);
                            var entry = latestState.Entries.TryGetValue(candidate.Key, out var entryValue)
                                ? entryValue
                                : new MediaGenerationEntry();

                            if (result.Success)
                            {
                                var targetVideoPath = candidate.LocalVideoPath ?? Path.Combine(playlistFolderPath, $"{candidate.Key}.mp4");
                                if (!string.IsNullOrWhiteSpace(result.OutputPath)
                                    && !string.Equals(result.OutputPath, targetVideoPath, StringComparison.OrdinalIgnoreCase)
                                    && File.Exists(result.OutputPath))
                                {
                                    try
                                    {
                                        Directory.CreateDirectory(Path.GetDirectoryName(targetVideoPath) ?? playlistFolderPath);
                                        File.Move(result.OutputPath, targetVideoPath, true);
                                    }
                                    catch (Exception ex)
                                    {
                                        entry.Status = "failed";
                                        entry.LastError = $"Failed to move output to {targetVideoPath}: {ex.Message}";
                                        entry.LastFinishedAtUtc = DateTimeOffset.UtcNow;
                                        summary.Failed++;
                                        latestState.Entries[candidate.Key] = entry;
                                        SaveMediaGenerationState(playlistFolderPath, latestState);
                                        global::System.Console.WriteLine($"render failed playlist={playlist.Id} key={candidate.Key} error={entry.LastError}");
                                        return;
                                    }
                                }

                                entry.Status = "completed";
                                entry.LastError = null;
                                entry.LastFinishedAtUtc = DateTimeOffset.UtcNow;
                                entry.VideoPath = targetVideoPath;
                                summary.Scheduled++;
                            }
                            else
                            {
                                entry.Status = "failed";
                                entry.LastError = result.ErrorMessage;
                                entry.LastFinishedAtUtc = DateTimeOffset.UtcNow;
                                summary.Failed++;
                                global::System.Console.WriteLine($"render failed playlist={playlist.Id} key={candidate.Key} error={result.ErrorMessage}");
                            }

                            latestState.Entries[candidate.Key] = entry;
                            SaveMediaGenerationState(playlistFolderPath, latestState);
                        }
                        finally
                        {
                            playlistStateLock.Release();
                        }
                    }
                    finally
                    {
                        renderSemaphore.Release();
                    }
                }));
            }

            await Task.WhenAll(renderTasks);
        }

        global::System.Console.WriteLine(
            $"generate-media summary playlists_scanned={summary.PlaylistsScanned} playlists_locked={summary.PlaylistsLocked} candidates={summary.CandidatesFound} scheduled={summary.Scheduled} failed={summary.Failed} skipped_completed={summary.SkippedCompleted} skipped_in_progress={summary.SkippedInProgress}");
    }

    public async Task RunGenerateImageAsync(string[] commandArgs)
    {
        if (commandArgs.Length < 2)
        {
            global::System.Console.WriteLine("Usage: generate-image <playlistId> <position> [model]");
            return;
        }

        if (!Guid.TryParse(commandArgs[0], out var playlistId))
        {
            global::System.Console.WriteLine("Invalid playlistId.");
            return;
        }

        if (!int.TryParse(commandArgs[1], out var position) || position <= 0)
        {
            global::System.Console.WriteLine("Invalid position. Must be a positive integer.");
            return;
        }

        var modelOverride = commandArgs.Length >= 3 ? commandArgs[2] : null;
        await GenerateImageForPositionAsync(playlistId, position, modelOverride);
    }

    public async Task RunGenerateYoutubePlaylistAsync(string[] commandArgs)
    {
        if (commandArgs.Length < 1)
        {
            global::System.Console.WriteLine("Usage: generate-youtube-playlist <playlistId> [privacy]");
            return;
        }

        if (!Guid.TryParse(commandArgs[0], out var playlistId))
        {
            global::System.Console.WriteLine("Invalid playlistId.");
            return;
        }

        var privacyOverride = commandArgs.Length >= 2 ? commandArgs[1]?.Trim() : null;
        if (!string.IsNullOrWhiteSpace(privacyOverride) &&
            !string.Equals(privacyOverride, "private", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(privacyOverride, "unlisted", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(privacyOverride, "public", StringComparison.OrdinalIgnoreCase))
        {
            global::System.Console.WriteLine("Invalid privacy. Allowed: private, unlisted, public.");
            return;
        }

        await GenerateYoutubePlaylistAsync(
            playlistId,
            string.IsNullOrWhiteSpace(privacyOverride) ? null : privacyOverride.ToLowerInvariant());
    }

    public async Task RunUploadYoutubeVideoAsync(string[] commandArgs)
    {
        if (commandArgs.Length < 2)
        {
            global::System.Console.WriteLine("Usage: upload-youtube-video <playlistId> <position>");
            return;
        }

        if (!Guid.TryParse(commandArgs[0], out var playlistId))
        {
            global::System.Console.WriteLine("Invalid playlistId.");
            return;
        }

        if (!int.TryParse(commandArgs[1], out var position) || position <= 0)
        {
            global::System.Console.WriteLine("Invalid position. Must be a positive integer.");
            return;
        }

        await UploadYoutubeVideoAsync(playlistId, position);
    }

    public async Task RunUploadYoutubeThumbnailAsync(string[] commandArgs)
    {
        if (commandArgs.Length < 2)
        {
            global::System.Console.WriteLine("Usage: upload-youtube-thumbnail <playlistId> <position>");
            return;
        }

        if (!Guid.TryParse(commandArgs[0], out var playlistId))
        {
            global::System.Console.WriteLine("Invalid playlistId.");
            return;
        }

        if (!int.TryParse(commandArgs[1], out var position) || position <= 0)
        {
            global::System.Console.WriteLine("Invalid position. Must be a positive integer.");
            return;
        }

        await UploadYoutubeThumbnailAsync(playlistId, position);
    }

    public async Task RunUploadYoutubeVideoWithThumbnailAsync(string[] commandArgs)
    {
        if (commandArgs.Length < 2)
        {
            global::System.Console.WriteLine("Usage: upload-youtube-video-with-thumbnail <playlistId> <position>");
            return;
        }

        if (!Guid.TryParse(commandArgs[0], out var playlistId))
        {
            global::System.Console.WriteLine("Invalid playlistId.");
            return;
        }

        if (!int.TryParse(commandArgs[1], out var position) || position <= 0)
        {
            global::System.Console.WriteLine("Invalid position. Must be a positive integer.");
            return;
        }

        await UploadYoutubeVideoAsync(playlistId, position);
        await UploadYoutubeThumbnailAsync(playlistId, position);
    }

    public async Task RunAddYoutubeVideosToPlaylistAsync(string[] commandArgs)
    {
        if (commandArgs.Length < 1)
        {
            global::System.Console.WriteLine("Usage: add-youtube-videos-to-playlist <playlistId>");
            return;
        }

        if (!Guid.TryParse(commandArgs[0], out var playlistId))
        {
            global::System.Console.WriteLine("Invalid playlistId.");
            return;
        }

        await AddYoutubeVideosToPlaylistAsync(playlistId);
    }

    public async Task RunTrackCreateYoutubeVideoThumbnailAsync(string[] commandArgs)
    {
        if (commandArgs.Length < 1)
        {
            global::System.Console.WriteLine("Usage: track-create-youtube-video-thumbnail <playlistId> [position]");
            return;
        }

        if (!Guid.TryParse(commandArgs[0], out var playlistId))
        {
            global::System.Console.WriteLine("Invalid playlistId.");
            return;
        }

        if (commandArgs.Length == 1)
        {
            var positions = await _context.Tracks
                .AsNoTracking()
                .Where(t => t.PlaylistId == playlistId)
                .OrderBy(t => t.PlaylistPosition)
                .Select(t => t.PlaylistPosition)
                .Distinct()
                .ToListAsync();

            if (positions.Count == 0)
            {
                global::System.Console.WriteLine("No tracks found for playlist.");
                return;
            }

            foreach (var trackPosition in positions)
            {
                await CreateYoutubeVideoThumbnailAsync(playlistId, trackPosition);
            }

            return;
        }

        if (!int.TryParse(commandArgs[1], out var position) || position <= 0)
        {
            global::System.Console.WriteLine("Invalid position. Must be a positive integer.");
            return;
        }

        await CreateYoutubeVideoThumbnailAsync(playlistId, position);
    }

    private async Task PrintAllPlaylistsAsync()
    {
        var playlists = await _context.Playlists
            .AsNoTracking()
            .OrderByDescending(p => p.CreatedAtUtc)
            .ToListAsync();

        _logger.LogInformation("🎵 Total playlists: {Count}\n", playlists.Count);

        if (playlists.Count == 0)
        {
            _logger.LogInformation("No playlists found in database.");
            return;
        }

        for (var i = 0; i < playlists.Count; i++)
        {
            var playlist = playlists[i];
            _logger.LogInformation(
                "{Index}. {Title} | Theme: {Theme} | Status: {Status} | Tracks: {TrackCount} | Created: {CreatedAt}",
                i + 1,
                playlist.Title,
                string.IsNullOrWhiteSpace(playlist.Theme) ? "-" : playlist.Theme,
                playlist.Status,
                playlist.TrackCount,
                playlist.CreatedAtUtc);
        }
    }

    private async Task PrintAllYoutubePlaylistsAsync()
    {
        var youtubePlaylists = await _context.YoutubePlaylists
            .AsNoTracking()
            .OrderByDescending(p => p.CreatedAtUtc)
            .ToListAsync();

        _logger.LogInformation("▶️ Total YouTube playlists: {Count}\n", youtubePlaylists.Count);

        if (youtubePlaylists.Count == 0)
        {
            _logger.LogInformation("No YouTube playlists found in database.");
            return;
        }

        for (var i = 0; i < youtubePlaylists.Count; i++)
        {
            var playlist = youtubePlaylists[i];
            _logger.LogInformation(
                "{Index}. {Title} | YoutubeId: {YoutubePlaylistId} | Privacy: {PrivacyStatus} | Items: {ItemCount} | Created: {CreatedAt}",
                i + 1,
                string.IsNullOrWhiteSpace(playlist.Title) ? "-" : playlist.Title,
                playlist.YoutubePlaylistId,
                string.IsNullOrWhiteSpace(playlist.PrivacyStatus) ? "-" : playlist.PrivacyStatus,
                playlist.ItemCount ?? 0,
                playlist.CreatedAtUtc);
        }
    }

    private async Task RunPlaylistPipelineAsync()
    {
        var workingDirectory = Environment.GetEnvironmentVariable("YT_PRODUCER_PLAYLIST_WORKING_DIRECTORY");
        if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            _logger.LogWarning("YT_PRODUCER_PLAYLIST_WORKING_DIRECTORY is not configured. Skipping pipeline.");
            return;
        }

        Directory.CreateDirectory(workingDirectory);
        _logger.LogInformation("Pipeline working directory: {WorkingDirectory}", workingDirectory);

        var playlists = await _context.Playlists
            .AsNoTracking()
            .OrderByDescending(p => p.CreatedAtUtc)
            .ToListAsync();

        if (playlists.Count == 0)
        {
            _logger.LogInformation("No playlists found. Nothing to process.");
            return;
        }

        var pipelineStats = new PipelineStats();

        foreach (var playlist in playlists)
        {
            var folderName = GetPlaylistFolderName(playlist.Id);
            var playlistFolderPath = Path.Combine(workingDirectory, folderName);
            var playlistMetadataPrompts = ParsePromptMetadata(playlist.Metadata);
            var trackPromptsByPosition = await GetTrackPromptsByPositionAsync(playlist.Id);

            if (!Directory.Exists(playlistFolderPath))
            {
                Directory.CreateDirectory(playlistFolderPath);
                pipelineStats.CreatedFolders++;
            }
            else
            {
                pipelineStats.ExistingFolders++;
            }

            var fileNames = Directory.EnumerateFiles(playlistFolderPath)
                .Select(Path.GetFileName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Cast<string>()
                .ToArray();

            var expectedTrackCount = Math.Max(playlist.TrackCount, 0);
            var missingImages = new List<int>();
            var missingTracks = new List<int>();

            for (var position = 1; position <= expectedTrackCount; position++)
            {
                if (!HasMediaForPosition(fileNames, position, ImageExtensions))
                {
                    missingImages.Add(position);
                }

                if (!HasMediaForPosition(fileNames, position, AudioExtensions))
                {
                    missingTracks.Add(position);
                }
            }

            WriteImagePromptFile(
                playlistFolderPath,
                playlist.Title,
                playlist.Theme,
                missingImages,
                trackPromptsByPosition,
                playlistMetadataPrompts.ImagePrompt);
            WriteSunoPromptFile(
                playlistFolderPath,
                playlist.Title,
                playlist.Theme,
                missingTracks,
                trackPromptsByPosition,
                playlistMetadataPrompts.SunoPrompt);

            pipelineStats.PlaylistsProcessed++;
            pipelineStats.TotalMissingImages += missingImages.Count;
            pipelineStats.TotalMissingTracks += missingTracks.Count;

            _logger.LogInformation(
                "{PlaylistTitle} -> Folder: {FolderName} | Missing images: {MissingImages} | Missing tracks: {MissingTracks}",
                string.IsNullOrWhiteSpace(playlist.Title) ? "-" : playlist.Title,
                folderName,
                missingImages.Count,
                missingTracks.Count);
        }

        _logger.LogInformation(
            "Pipeline summary | Processed: {Processed} | New folders: {Created} | Existing folders: {Existing} | Missing images: {MissingImages} | Missing tracks: {MissingTracks}",
            pipelineStats.PlaylistsProcessed,
            pipelineStats.CreatedFolders,
            pipelineStats.ExistingFolders,
            pipelineStats.TotalMissingImages,
            pipelineStats.TotalMissingTracks);
    }

    private static string GetPlaylistFolderName(Guid playlistId)
    {
        return playlistId.ToString();
    }

    private async Task GenerateImageForPositionAsync(Guid playlistId, int position, string? modelOverride)
    {
        var workingDirectory = Environment.GetEnvironmentVariable("YT_PRODUCER_PLAYLIST_WORKING_DIRECTORY");
        if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            global::System.Console.WriteLine("YT_PRODUCER_PLAYLIST_WORKING_DIRECTORY is not configured.");
            return;
        }

        var mcpWorkingDirectory = Environment.GetEnvironmentVariable("YT_PRODUCER_MCP_KIEAI_WORKING_DIRECTORY");
        if (string.IsNullOrWhiteSpace(mcpWorkingDirectory))
        {
            global::System.Console.WriteLine("YT_PRODUCER_MCP_KIEAI_WORKING_DIRECTORY is not configured.");
            return;
        }

        var mcpProject = Environment.GetEnvironmentVariable("YT_PRODUCER_MCP_KIEAI_PROJECT")
            ?? "OnlineTeamTools.MCP.KieAi/OnlineTeamTools.MCP.KieAi.csproj";

        var model = string.IsNullOrWhiteSpace(modelOverride)
            ? (Environment.GetEnvironmentVariable("YT_PRODUCER_KIEAI_IMAGE_MODEL") ?? "z-image")
            : modelOverride.Trim();
        var aspectRatio = Environment.GetEnvironmentVariable("YT_PRODUCER_KIEAI_IMAGE_ASPECT_RATIO") ?? "16:9";

        var playlist = await _context.Playlists
            .AsNoTracking()
            .Include(p => p.Tracks)
            .FirstOrDefaultAsync(p => p.Id == playlistId);

        if (playlist == null)
        {
            global::System.Console.WriteLine("Playlist not found.");
            return;
        }

        var playlistFolderPath = Path.Combine(workingDirectory, GetPlaylistFolderName(playlist.Id));
        if (!Directory.Exists(playlistFolderPath))
        {
            global::System.Console.WriteLine($"Playlist folder not found: {playlistFolderPath}");
            return;
        }

        var playlistMetadataPrompts = ParsePromptMetadata(playlist.Metadata);
        var track = playlist.Tracks.FirstOrDefault(t => t.PlaylistPosition == position);
        var trackMetadata = track != null ? ParsePromptMetadata(track.Metadata) : new PromptMetadata(null, null);

        var prompt = trackMetadata.ImagePrompt ?? playlistMetadataPrompts.ImagePrompt;
        if (string.IsNullOrWhiteSpace(prompt))
        {
            global::System.Console.WriteLine("Image prompt is empty. Skipping.");
            return;
        }

        var outputFilePath = GetNextImageOutputPath(playlistFolderPath, position);
        global::System.Console.WriteLine($"image_generation start playlist={playlistId} position={position}");

        var result = await ExecuteImageGenerationAsync(
            mcpWorkingDirectory,
            mcpProject,
            outputFilePath,
            prompt,
            aspectRatio,
            model);

        if (!result.Success)
        {
            global::System.Console.WriteLine($"image_generation failed playlist={playlistId} position={position} error={result.ErrorMessage}");
            return;
        }

        await PersistTrackImagesAsync(playlist, track, position, prompt, model, aspectRatio, result);

        global::System.Console.WriteLine(
            $"image_generation complete playlist={playlistId} position={position} files={result.SavedFiles.Count}");
    }

    private async Task GenerateYoutubePlaylistAsync(Guid playlistId, string? privacyOverride = null)
    {
        var mcpWorkingDirectory = Environment.GetEnvironmentVariable("YT_PRODUCER_MCP_YOUTUBE_WORKING_DIRECTORY");
        if (string.IsNullOrWhiteSpace(mcpWorkingDirectory))
        {
            global::System.Console.WriteLine("YT_PRODUCER_MCP_YOUTUBE_WORKING_DIRECTORY is not configured.");
            return;
        }

        var mcpProject = Environment.GetEnvironmentVariable("YT_PRODUCER_MCP_YOUTUBE_PROJECT")
            ?? "OnlineTeamTools.MCP.YouTube/OnlineTeamTools.MCP.YouTube.csproj";

        var privacy = privacyOverride
            ?? Environment.GetEnvironmentVariable("YT_PRODUCER_YOUTUBE_PLAYLIST_PRIVACY")
            ?? "unlisted";

        var playlist = await _context.Playlists.FirstOrDefaultAsync(p => p.Id == playlistId);
        if (playlist == null)
        {
            global::System.Console.WriteLine("Playlist not found.");
            return;
        }

        if (!string.IsNullOrWhiteSpace(playlist.YoutubePlaylistId))
        {
            var lookup = await ExecuteYoutubeGetPlaylistAsync(
                mcpWorkingDirectory,
                mcpProject,
                playlist.YoutubePlaylistId);

            if (lookup.Success && !string.IsNullOrWhiteSpace(lookup.PlaylistId))
            {
                await UpsertYoutubePlaylistRecordAsync(playlist, lookup.PlaylistId, lookup);
                global::System.Console.WriteLine(
                    $"youtube playlist verified: {lookup.PlaylistId}");
                return;
            }

            global::System.Console.WriteLine(
                $"Warning: youtube playlist id {playlist.YoutubePlaylistId} not found. Creating new playlist.");
        }

        var title = playlist.Title;
        var description = playlist.Description;

        var result = await ExecuteYoutubeCreatePlaylistAsync(
            mcpWorkingDirectory,
            mcpProject,
            title,
            description,
            privacy);

        if (!result.Success || string.IsNullOrWhiteSpace(result.PlaylistId))
        {
            global::System.Console.WriteLine($"youtube.create_playlist failed: {result.ErrorMessage}");
            return;
        }

        playlist.YoutubePlaylistId = result.PlaylistId;
        playlist.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await UpsertYoutubePlaylistRecordAsync(playlist, result.PlaylistId, null, privacy);
        await _context.SaveChangesAsync();

        global::System.Console.WriteLine($"youtube playlist created: {result.PlaylistId}");
    }

    private async Task UploadYoutubeVideoAsync(Guid playlistId, int position)
    {
        var workingDirectory = Environment.GetEnvironmentVariable("YT_PRODUCER_PLAYLIST_WORKING_DIRECTORY");
        if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            global::System.Console.WriteLine("YT_PRODUCER_PLAYLIST_WORKING_DIRECTORY is not configured.");
            return;
        }

        var mcpWorkingDirectory = Environment.GetEnvironmentVariable("YT_PRODUCER_MCP_YOUTUBE_WORKING_DIRECTORY");
        if (string.IsNullOrWhiteSpace(mcpWorkingDirectory))
        {
            global::System.Console.WriteLine("YT_PRODUCER_MCP_YOUTUBE_WORKING_DIRECTORY is not configured.");
            return;
        }

        var mcpProject = Environment.GetEnvironmentVariable("YT_PRODUCER_MCP_YOUTUBE_PROJECT")
            ?? "OnlineTeamTools.MCP.YouTube/OnlineTeamTools.MCP.YouTube.csproj";

        var allowedRoot = Environment.GetEnvironmentVariable("YT_PRODUCER_YOUTUBE_ALLOWED_ROOT")
            ?? Environment.GetEnvironmentVariable("YT_PRODUCER_PLAYLIST_WORKING_DIRECTORY");

        var playlist = await _context.Playlists
            .Include(p => p.Tracks)
            .FirstOrDefaultAsync(p => p.Id == playlistId);
        if (playlist == null)
        {
            global::System.Console.WriteLine("Playlist not found.");
            return;
        }

        var track = playlist.Tracks.FirstOrDefault(t => t.PlaylistPosition == position);
        if (track == null)
        {
            global::System.Console.WriteLine("Track not found for position.");
            return;
        }

        var existing = await _context.Set<TrackOnYoutube>()
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.TrackId == track.Id);
        if (existing != null)
        {
            global::System.Console.WriteLine($"Track already uploaded: {existing.VideoId}");
            return;
        }

        var playlistFolderPath = Path.Combine(workingDirectory, GetPlaylistFolderName(playlistId));
        var videoPath = ResolveVideoPathForPosition(playlistFolderPath, position);
        if (videoPath == null)
        {
            global::System.Console.WriteLine($"Video file not found for position {position}.");
            return;
        }

        var title = track.YouTubeTitle ?? track.Title;
        var description = ResolveYoutubeDescription(track, playlist);
        var publishState = await GetOrCreateYoutubeLastPublishedDateAsync();
        var publishAt = GetNextYoutubePublishSlotUtc(publishState.LastPublishedDate);
        var scheduledPrivacy = "private";

        var result = await ExecuteYoutubeUploadVideoAsync(
            mcpWorkingDirectory,
            mcpProject,
            allowedRoot,
            videoPath,
            title,
            description,
            scheduledPrivacy,
            publishAt);

        if (!result.Success || string.IsNullOrWhiteSpace(result.VideoId))
        {
            global::System.Console.WriteLine($"youtube.upload_video failed: {result.ErrorMessage}");
            return;
        }

        var record = new TrackOnYoutube
        {
            Id = Guid.NewGuid(),
            TrackId = track.Id,
            PlaylistId = playlistId,
            PlaylistPosition = position,
            VideoId = result.VideoId,
            Url = result.Url,
            Title = title,
            Description = description,
            Privacy = scheduledPrivacy,
            FilePath = videoPath,
            Status = "uploaded",
            Metadata = null,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        publishState.LastPublishedDate = publishAt;
        publishState.VideoId = result.VideoId;
        _context.Add(record);
        await _context.SaveChangesAsync();

        global::System.Console.WriteLine($"youtube upload complete: {result.VideoId} scheduled_at_utc={publishAt:yyyy-MM-ddTHH:mm:ssZ}");
    }

    private async Task UploadYoutubeThumbnailAsync(Guid playlistId, int position)
    {
        var workingDirectory = Environment.GetEnvironmentVariable("YT_PRODUCER_PLAYLIST_WORKING_DIRECTORY");
        if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            global::System.Console.WriteLine("YT_PRODUCER_PLAYLIST_WORKING_DIRECTORY is not configured.");
            return;
        }

        var mcpWorkingDirectory = Environment.GetEnvironmentVariable("YT_PRODUCER_MCP_YOUTUBE_WORKING_DIRECTORY");
        if (string.IsNullOrWhiteSpace(mcpWorkingDirectory))
        {
            global::System.Console.WriteLine("YT_PRODUCER_MCP_YOUTUBE_WORKING_DIRECTORY is not configured.");
            return;
        }

        var mcpProject = Environment.GetEnvironmentVariable("YT_PRODUCER_MCP_YOUTUBE_PROJECT")
            ?? "OnlineTeamTools.MCP.YouTube/OnlineTeamTools.MCP.YouTube.csproj";

        var allowedRoot = Environment.GetEnvironmentVariable("YT_PRODUCER_YOUTUBE_ALLOWED_ROOT")
            ?? Environment.GetEnvironmentVariable("YT_PRODUCER_PLAYLIST_WORKING_DIRECTORY");

        var playlistFolderPath = Path.Combine(workingDirectory, GetPlaylistFolderName(playlistId));
        if (!Directory.Exists(playlistFolderPath))
        {
            global::System.Console.WriteLine($"Playlist folder not found: {playlistFolderPath}");
            return;
        }

        var imagePath = ResolveYoutubeThumbnailPathForPosition(playlistFolderPath, position)
            ?? ResolveImagePathForPosition(playlistFolderPath, position);
        if (imagePath == null)
        {
            global::System.Console.WriteLine($"Image file not found for position {position}.");
            return;
        }

        var fileInfo = new FileInfo(imagePath);
        if (fileInfo.Length > 2L * 1024 * 1024 * 1024)
        {
            global::System.Console.WriteLine("Thumbnail file exceeds 2GB limit.");
            return;
        }

        var track = await _context.Tracks
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.PlaylistId == playlistId && t.PlaylistPosition == position);
        if (track == null)
        {
            global::System.Console.WriteLine("Track not found for position.");
            return;
        }

        var youtubeRecord = await _context.Set<TrackOnYoutube>()
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.TrackId == track.Id);
        if (youtubeRecord == null || string.IsNullOrWhiteSpace(youtubeRecord.VideoId))
        {
            global::System.Console.WriteLine("No YouTube video record found for this track. Upload video first.");
            return;
        }

        var result = await ExecuteYoutubeUploadThumbnailAsync(
            mcpWorkingDirectory,
            mcpProject,
            allowedRoot,
            youtubeRecord.VideoId,
            imagePath);

        if (!result.Success)
        {
            global::System.Console.WriteLine($"youtube.upload_thumbnail failed: {result.ErrorMessage}");
            return;
        }

        global::System.Console.WriteLine($"youtube thumbnail uploaded: {youtubeRecord.VideoId}");
    }

    private async Task AddYoutubeVideosToPlaylistAsync(Guid playlistId)
    {
        var mcpWorkingDirectory = Environment.GetEnvironmentVariable("YT_PRODUCER_MCP_YOUTUBE_WORKING_DIRECTORY");
        if (string.IsNullOrWhiteSpace(mcpWorkingDirectory))
        {
            global::System.Console.WriteLine("YT_PRODUCER_MCP_YOUTUBE_WORKING_DIRECTORY is not configured.");
            return;
        }

        var mcpProject = Environment.GetEnvironmentVariable("YT_PRODUCER_MCP_YOUTUBE_PROJECT")
            ?? "OnlineTeamTools.MCP.YouTube/OnlineTeamTools.MCP.YouTube.csproj";

        var playlist = await _context.Playlists
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == playlistId);
        if (playlist == null)
        {
            global::System.Console.WriteLine("Playlist not found.");
            return;
        }

        string? youtubePlaylistId = null;
        var youtubePlaylistRecord = await _context.YoutubePlaylists
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.PlaylistId == playlistId);
        if (youtubePlaylistRecord != null && !string.IsNullOrWhiteSpace(youtubePlaylistRecord.YoutubePlaylistId))
        {
            youtubePlaylistId = youtubePlaylistRecord.YoutubePlaylistId;
        }
        else if (!string.IsNullOrWhiteSpace(playlist.YoutubePlaylistId))
        {
            youtubePlaylistId = playlist.YoutubePlaylistId;
        }

        if (string.IsNullOrWhiteSpace(youtubePlaylistId))
        {
            global::System.Console.WriteLine("YouTube playlist id is missing. Run generate-youtube-playlist first.");
            return;
        }

        var trackVideos = await _context.TrackOnYoutube
            .AsNoTracking()
            .Where(x => x.PlaylistId == playlistId)
            .OrderBy(x => x.PlaylistPosition)
            .ThenBy(x => x.CreatedAtUtc)
            .ToListAsync();

        var videoIds = trackVideos
            .Select(x => x.VideoId?.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .Cast<string>()
            .ToList();

        if (videoIds.Count == 0)
        {
            global::System.Console.WriteLine("No uploaded videos found in track_on_youtube for this playlist.");
            return;
        }

        var result = await ExecuteYoutubeAddVideosToPlaylistAsync(
            mcpWorkingDirectory,
            mcpProject,
            youtubePlaylistId,
            videoIds);

        if (!result.Success)
        {
            global::System.Console.WriteLine($"youtube.add_videos_to_playlist failed: {result.ErrorMessage}");
            return;
        }

        global::System.Console.WriteLine(
            $"youtube playlist add complete: playlist_id={youtubePlaylistId} added={result.AddedCount}");
    }

    private async Task CreateYoutubeVideoThumbnailAsync(Guid playlistId, int position)
    {
        var workingDirectory = Environment.GetEnvironmentVariable("YT_PRODUCER_PLAYLIST_WORKING_DIRECTORY");
        if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            global::System.Console.WriteLine("YT_PRODUCER_PLAYLIST_WORKING_DIRECTORY is not configured.");
            return;
        }

        var mcpMediaWorkingDirectory = Environment.GetEnvironmentVariable("YT_PRODUCER_MCP_MEDIA_WORKING_DIRECTORY");
        if (string.IsNullOrWhiteSpace(mcpMediaWorkingDirectory))
        {
            global::System.Console.WriteLine("YT_PRODUCER_MCP_MEDIA_WORKING_DIRECTORY is not configured.");
            return;
        }

        var mcpMediaProject = Environment.GetEnvironmentVariable("YT_PRODUCER_MCP_MEDIA_PROJECT")
            ?? "OnlineTeamTools.MCP.Media/OnlineTeamTools.MCP.Media.csproj";
        var toolName = Environment.GetEnvironmentVariable("YT_PRODUCER_MCP_MEDIA_THUMBNAIL_TOOL")
            ?? "media.create_youtube_thumbnail";

        var playlist = await _context.Playlists
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == playlistId);
        if (playlist == null)
        {
            global::System.Console.WriteLine("Playlist not found.");
            return;
        }

        var track = await _context.Tracks
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.PlaylistId == playlistId && t.PlaylistPosition == position);
        if (track == null)
        {
            global::System.Console.WriteLine("Track not found for position.");
            return;
        }

        var playlistFolderPath = Path.Combine(workingDirectory, GetPlaylistFolderName(playlistId));
        if (!Directory.Exists(playlistFolderPath))
        {
            global::System.Console.WriteLine($"Playlist folder not found: {playlistFolderPath}");
            return;
        }

        var imagePath = ResolveImagePathForPosition(playlistFolderPath, position);
        if (imagePath == null)
        {
            global::System.Console.WriteLine($"Image file not found for position {position}.");
            return;
        }

        var extension = Path.GetExtension(imagePath);
        if (string.IsNullOrWhiteSpace(extension))
        {
            extension = ".png";
        }

        var outputPath = Path.Combine(playlistFolderPath, $"{position}_thumbnail{extension.ToLowerInvariant()}");
        if (File.Exists(outputPath))
        {
            try
            {
                File.Delete(outputPath);
            }
            catch (Exception ex)
            {
                global::System.Console.WriteLine(
                    $"track thumbnail generation failed playlist={playlistId} position={position} error=failed to delete existing thumbnail: {ex.Message}");
                return;
            }
        }

        var logoPath = Environment.GetEnvironmentVariable("YT_PRODUCER_THUMBNAIL_LOGO_PATH");
        var headlineOverride = Environment.GetEnvironmentVariable("YT_PRODUCER_THUMBNAIL_HEADLINE");
        var subheadlineOverride = Environment.GetEnvironmentVariable("YT_PRODUCER_THUMBNAIL_SUBHEADLINE");
        var headlineFont = Environment.GetEnvironmentVariable("YT_PRODUCER_THUMBNAIL_HEADLINE_FONT");
        var subheadlineFont = Environment.GetEnvironmentVariable("YT_PRODUCER_THUMBNAIL_SUBHEADLINE_FONT");
        var headlineColor = Environment.GetEnvironmentVariable("YT_PRODUCER_THUMBNAIL_HEADLINE_COLOR");
        var subheadlineColor = Environment.GetEnvironmentVariable("YT_PRODUCER_THUMBNAIL_SUBHEADLINE_COLOR");
        var visualStyleHint = TryGetMetadataString(track.Metadata, "visualStyleHint")
            ?? TryGetMetadataString(playlist.Metadata, "visualStyleHint");
        var headline = ResolveThumbnailHeadline(track, playlist, headlineOverride);
        var subheadline = ResolveThumbnailSubheadline(track, playlist, subheadlineOverride);
        headlineFont = ResolveThumbnailHeadlineFont(headlineFont, visualStyleHint);
        subheadlineFont = ResolveThumbnailSubheadlineFont(subheadlineFont, visualStyleHint);

        var result = await ExecuteCreateYoutubeVideoThumbnailAsync(
            mcpMediaWorkingDirectory,
            mcpMediaProject,
            toolName,
            imagePath,
            outputPath,
            logoPath,
            headline,
            subheadline,
            headlineFont,
            subheadlineFont,
            headlineColor,
            subheadlineColor);

        if (!result.Success)
        {
            global::System.Console.WriteLine(
                $"track thumbnail generation failed playlist={playlistId} position={position} error={result.ErrorMessage}");
            return;
        }

        global::System.Console.WriteLine(
            $"track thumbnail generated playlist={playlistId} position={position} file={result.OutputPath ?? outputPath}");
    }

    private static string ResolveThumbnailHeadline(Track track, Playlist playlist, string? headlineOverride)
    {
        if (!string.IsNullOrWhiteSpace(headlineOverride))
        {
            return headlineOverride.Trim();
        }

        var hint = TryGetMetadataString(track.Metadata, "thumbnailTextHint")
            ?? TryGetMetadataString(playlist.Metadata, "thumbnailTextHint");
        if (!string.IsNullOrWhiteSpace(hint))
        {
            return NormalizeHeadline(hint);
        }

        var hookType = TryGetMetadataString(track.Metadata, "hookType");
        if (!string.IsNullOrWhiteSpace(hookType))
        {
            var firstWord = hookType
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(firstWord))
            {
                return NormalizeHeadline(firstWord);
            }
        }

        return "FOCUS";
    }

    private static string ResolveThumbnailSubheadline(Track track, Playlist playlist, string? subheadlineOverride)
    {
        if (!string.IsNullOrWhiteSpace(subheadlineOverride))
        {
            return subheadlineOverride.Trim();
        }

        var scenario = TryGetMetadataString(track.Metadata, "listeningScenario")
            ?? TryGetMetadataString(track.Metadata, "playlistCategory")
            ?? TryGetMetadataString(playlist.Metadata, "playlistCategory")
            ?? "workout";

        var musicPrompt = TryGetMetadataString(track.Metadata, "musicGenerationPrompt")
            ?? TryGetMetadataString(playlist.Metadata, "musicGenerationPrompt");
        var subgenre = TryExtractFieldFromPrompt(musicPrompt, "Subgenre")
            ?? TryExtractFieldFromPrompt(musicPrompt, "Genre");

        var scenarioToken = NormalizeHeadline(scenario);
        if (!string.IsNullOrWhiteSpace(subgenre))
        {
            return $"{NormalizeHeadline(subgenre)} {scenarioToken}";
        }

        return $"WORKOUT {scenarioToken}";
    }

    private static string ResolveThumbnailHeadlineFont(string? envFont, string? visualStyleHint)
    {
        if (!string.IsNullOrWhiteSpace(envFont))
        {
            return envFont.Trim();
        }

        if (!string.IsNullOrWhiteSpace(visualStyleHint) &&
            (visualStyleHint.Contains("contrast", StringComparison.OrdinalIgnoreCase) ||
             visualStyleHint.Contains("dramatic", StringComparison.OrdinalIgnoreCase) ||
             visualStyleHint.Contains("bold", StringComparison.OrdinalIgnoreCase)))
        {
            return "Bebas Neue";
        }

        return "Bebas Neue";
    }

    private static string ResolveThumbnailSubheadlineFont(string? envFont, string? visualStyleHint)
    {
        if (!string.IsNullOrWhiteSpace(envFont))
        {
            return envFont.Trim();
        }

        if (!string.IsNullOrWhiteSpace(visualStyleHint) &&
            visualStyleHint.Contains("clean", StringComparison.OrdinalIgnoreCase))
        {
            return "Montserrat";
        }

        return "Montserrat";
    }

    private static string NormalizeHeadline(string value)
    {
        var cleaned = Regex.Replace(value ?? string.Empty, "[^a-zA-Z0-9\\s]+", " ");
        cleaned = Regex.Replace(cleaned, "\\s+", " ").Trim();
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return "FOCUS";
        }

        return cleaned.ToUpperInvariant();
    }

    private static string? TryExtractFieldFromPrompt(string? prompt, string field)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return null;
        }

        var regex = new Regex($"{Regex.Escape(field)}\\s*:\\s*(?<value>[^\\.\\n\\r]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var match = regex.Match(prompt);
        if (!match.Success)
        {
            return null;
        }

        var value = match.Groups["value"].Value.Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static string GetNextImageOutputPath(string playlistFolderPath, int position)
    {
        var existing = Directory.EnumerateFiles(playlistFolderPath)
            .Where(path =>
            {
                var name = Path.GetFileName(path);
                if (string.IsNullOrWhiteSpace(name) || name.StartsWith("._", StringComparison.Ordinal))
                {
                    return false;
                }

                var extension = Path.GetExtension(name);
                return ImageExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase)
                    && TryMatchPosition(name, position);
            })
            .ToList();

        var extensionChoice = existing
            .Select(Path.GetExtension)
            .FirstOrDefault(ext => !string.IsNullOrWhiteSpace(ext))
            ?? ".png";

        return Path.Combine(playlistFolderPath, $"{position}{extensionChoice}");
    }

    private static bool TryMatchPosition(string fileName, int position)
    {
        var baseName = Path.GetFileNameWithoutExtension(fileName);
        var match = Regex.Match(baseName, "^(?<pos>\\d+)(?:_(?<variant>\\d+))?$", RegexOptions.CultureInvariant);
        if (!match.Success)
        {
            return false;
        }

        return int.TryParse(match.Groups["pos"].Value, out var pos) && pos == position;
    }

    private static int ResolveNextImageIndex(IEnumerable<string> existingFiles, int position)
    {
        var indices = new HashSet<int>();
        foreach (var file in existingFiles)
        {
            var baseName = Path.GetFileNameWithoutExtension(file);
            var match = Regex.Match(baseName, "^(?<pos>\\d+)(?:_(?<variant>\\d+))?$", RegexOptions.CultureInvariant);
            if (!match.Success)
            {
                continue;
            }

            if (!int.TryParse(match.Groups["pos"].Value, out var pos) || pos != position)
            {
                continue;
            }

            if (match.Groups["variant"].Success && int.TryParse(match.Groups["variant"].Value, out var variant))
            {
                indices.Add(variant);
            }
            else
            {
                indices.Add(0);
            }
        }

        if (!indices.Contains(0))
        {
            return 0;
        }

        var next = 1;
        while (indices.Contains(next))
        {
            next++;
        }

        return next;
    }

    private static string BuildImageGenerationRequestJson(
        string outputFilePath,
        string prompt,
        string aspectRatio,
        string model)
    {
        var payload = new
        {
            jsonrpc = "2.0",
            id = 2,
            method = "tools/call",
            @params = new
            {
                name = "image_generation",
                arguments = new
                {
                    file = outputFilePath,
                    prompt,
                    aspect_ratio = aspectRatio,
                    model
                }
            }
        };

        return JsonSerializer.Serialize(payload);
    }

    private async Task<ImageGenerationExecutionResult> ExecuteImageGenerationAsync(
        string mcpWorkingDirectory,
        string mcpProject,
        string outputFilePath,
        string prompt,
        string aspectRatio,
        string model)
    {
        try
        {
            var requestJson = BuildImageGenerationRequestJson(outputFilePath, prompt, aspectRatio, model);
            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"run --project \"{mcpProject}\"",
                WorkingDirectory = mcpWorkingDirectory,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = startInfo };
            process.Start();
            await process.StandardInput.WriteLineAsync(requestJson);
            process.StandardInput.Close();
            var stdOut = await process.StandardOutput.ReadToEndAsync();
            var stdErr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                var error = string.IsNullOrWhiteSpace(stdErr) ? $"MCP process exit code {process.ExitCode}" : stdErr.Trim();
                return ImageGenerationExecutionResult.Fail(error);
            }

            var jsonLine = TryExtractJsonRpcLine(stdOut);
            if (string.IsNullOrWhiteSpace(jsonLine))
            {
                return ImageGenerationExecutionResult.Fail("MCP response did not contain JSON-RPC payload.");
            }

            using var responseDoc = JsonDocument.Parse(jsonLine);
            var root = responseDoc.RootElement;

            if (root.TryGetProperty("error", out var errorNode))
            {
                return ImageGenerationExecutionResult.Fail(errorNode.GetRawText());
            }

            var savedFiles = new List<SavedImageArtifact>();
            if (root.TryGetProperty("result", out var resultNode))
            {
                var sourceUrls = new List<string>();
                if (resultNode.TryGetProperty("resultUrls", out var resultUrlsNode) && resultUrlsNode.ValueKind == JsonValueKind.Array)
                {
                    foreach (var urlItem in resultUrlsNode.EnumerateArray())
                    {
                        if (urlItem.ValueKind == JsonValueKind.String)
                        {
                            var urlValue = urlItem.GetString();
                            if (!string.IsNullOrWhiteSpace(urlValue))
                            {
                                sourceUrls.Add(urlValue);
                            }
                        }
                    }
                }

                if (resultNode.TryGetProperty("savedFiles", out var savedNode) && savedNode.ValueKind == JsonValueKind.Array)
                {
                    var index = 0;
                    foreach (var item in savedNode.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.Object &&
                            item.TryGetProperty("filePath", out var filePathNode) &&
                            filePathNode.ValueKind == JsonValueKind.String)
                        {
                            var filePath = filePathNode.GetString();
                            if (!string.IsNullOrWhiteSpace(filePath))
                            {
                                savedFiles.Add(new SavedImageArtifact(
                                    filePath,
                                    Path.GetFileName(filePath),
                                    index < sourceUrls.Count ? sourceUrls[index] : null));
                            }
                        }
                        index++;
                    }
                }
            }

            return ImageGenerationExecutionResult.Ok(savedFiles);
        }
        catch (Exception ex)
        {
            return ImageGenerationExecutionResult.Fail(ex.Message);
        }
    }

    private static string BuildYoutubeCreatePlaylistRequestJson(
        string title,
        string? description,
        string privacy)
    {
        var payload = new
        {
            jsonrpc = "2.0",
            id = 10,
            method = "tools/call",
            @params = new
            {
                name = "youtube.create_playlist",
                arguments = new
                {
                    title,
                    description,
                    privacy
                }
            }
        };

        return JsonSerializer.Serialize(payload);
    }

    private async Task<YoutubePlaylistCreateResult> ExecuteYoutubeCreatePlaylistAsync(
        string mcpWorkingDirectory,
        string mcpProject,
        string title,
        string? description,
        string privacy)
    {
        try
        {
            var requestJson = BuildYoutubeCreatePlaylistRequestJson(title, description, privacy);
            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"run --project \"{mcpProject}\"",
                WorkingDirectory = mcpWorkingDirectory,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = startInfo };
            process.Start();
            await process.StandardInput.WriteLineAsync(requestJson);
            process.StandardInput.Close();
            var stdOut = await process.StandardOutput.ReadToEndAsync();
            var stdErr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                var error = string.IsNullOrWhiteSpace(stdErr) ? $"MCP process exit code {process.ExitCode}" : stdErr.Trim();
                return YoutubePlaylistCreateResult.Fail(error);
            }

            var jsonLine = TryExtractJsonRpcLine(stdOut);
            if (string.IsNullOrWhiteSpace(jsonLine))
            {
                return YoutubePlaylistCreateResult.Fail("MCP response did not contain JSON-RPC payload.");
            }

            using var responseDoc = JsonDocument.Parse(jsonLine);
            var root = responseDoc.RootElement;

            if (root.TryGetProperty("error", out var errorNode))
            {
                return YoutubePlaylistCreateResult.Fail(errorNode.GetRawText());
            }

            if (root.TryGetProperty("result", out var resultNode))
            {
                if (resultNode.TryGetProperty("data", out var dataNode))
                {
                    resultNode = dataNode;
                }

                if (resultNode.TryGetProperty("playlist_id", out var playlistIdNode) &&
                    playlistIdNode.ValueKind == JsonValueKind.String)
                {
                    var playlistId = playlistIdNode.GetString();
                    if (!string.IsNullOrWhiteSpace(playlistId))
                    {
                        return YoutubePlaylistCreateResult.Ok(playlistId);
                    }
                }
            }

            return YoutubePlaylistCreateResult.Fail("playlist_id not returned from MCP.");
        }
        catch (Exception ex)
        {
            return YoutubePlaylistCreateResult.Fail(ex.Message);
        }
    }

    private static string BuildYoutubeUploadVideoRequestJson(
        string filePath,
        string title,
        string? description,
        string privacy,
        DateTimeOffset? publishAt)
    {
        var payload = new
        {
            jsonrpc = "2.0",
            id = 11,
            method = "tools/call",
            @params = new
            {
                name = "youtube.upload_video",
                arguments = new
                {
                    file_path = filePath,
                    title,
                    description,
                    privacy,
                    publish_at = publishAt?.ToUniversalTime().ToString("O")
                }
            }
        };

        return JsonSerializer.Serialize(payload);
    }

    private async Task<YoutubeUploadVideoResult> ExecuteYoutubeUploadVideoAsync(
        string mcpWorkingDirectory,
        string mcpProject,
        string? allowedRoot,
        string filePath,
        string title,
        string? description,
        string privacy,
        DateTimeOffset? publishAt)
    {
        try
        {
            var requestJson = BuildYoutubeUploadVideoRequestJson(filePath, title, description, privacy, publishAt);
            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"run --project \"{mcpProject}\"",
                WorkingDirectory = mcpWorkingDirectory,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            if (!string.IsNullOrWhiteSpace(allowedRoot))
            {
                startInfo.Environment["YOUTUBE_ALLOWED_ROOT"] = allowedRoot;
            }

            using var process = new Process { StartInfo = startInfo };
            process.Start();
            await process.StandardInput.WriteLineAsync(requestJson);
            process.StandardInput.Close();
            var stdOut = await process.StandardOutput.ReadToEndAsync();
            var stdErr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                var error = string.IsNullOrWhiteSpace(stdErr) ? $"MCP process exit code {process.ExitCode}" : stdErr.Trim();
                return YoutubeUploadVideoResult.Fail(error);
            }

            var jsonLine = TryExtractJsonRpcLine(stdOut);
            if (string.IsNullOrWhiteSpace(jsonLine))
            {
                return YoutubeUploadVideoResult.Fail("MCP response did not contain JSON-RPC payload.");
            }

            using var responseDoc = JsonDocument.Parse(jsonLine);
            var root = responseDoc.RootElement;

            if (root.TryGetProperty("error", out var errorNode))
            {
                return YoutubeUploadVideoResult.Fail(errorNode.GetRawText());
            }

            if (root.TryGetProperty("result", out var resultNode))
            {
                if (resultNode.TryGetProperty("data", out var dataNode))
                {
                    resultNode = dataNode;
                }

                if (resultNode.TryGetProperty("video_id", out var videoIdNode) &&
                    videoIdNode.ValueKind == JsonValueKind.String)
                {
                    var videoId = videoIdNode.GetString();
                    var url = resultNode.TryGetProperty("url", out var urlNode) && urlNode.ValueKind == JsonValueKind.String
                        ? urlNode.GetString()
                        : null;
                    if (!string.IsNullOrWhiteSpace(videoId))
                    {
                        return YoutubeUploadVideoResult.Ok(videoId, url);
                    }
                }
            }

            return YoutubeUploadVideoResult.Fail("video_id not returned from MCP.");
        }
        catch (Exception ex)
        {
            return YoutubeUploadVideoResult.Fail(ex.Message);
        }
    }

    private static string BuildYoutubeGetPlaylistRequestJson(string playlistId)
    {
        var payload = new
        {
            jsonrpc = "2.0",
            id = 13,
            method = "tools/call",
            @params = new
            {
                name = "youtube.get_playlist",
                arguments = new
                {
                    playlist_id = playlistId
                }
            }
        };

        return JsonSerializer.Serialize(payload);
    }

    private async Task<YoutubeGetPlaylistResult> ExecuteYoutubeGetPlaylistAsync(
        string mcpWorkingDirectory,
        string mcpProject,
        string playlistId)
    {
        try
        {
            var requestJson = BuildYoutubeGetPlaylistRequestJson(playlistId);
            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"run --project \"{mcpProject}\"",
                WorkingDirectory = mcpWorkingDirectory,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = startInfo };
            process.Start();
            await process.StandardInput.WriteLineAsync(requestJson);
            process.StandardInput.Close();
            var stdOut = await process.StandardOutput.ReadToEndAsync();
            var stdErr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                var error = string.IsNullOrWhiteSpace(stdErr) ? $"MCP process exit code {process.ExitCode}" : stdErr.Trim();
                return YoutubeGetPlaylistResult.Fail(error);
            }

            var jsonLine = TryExtractJsonRpcLine(stdOut);
            if (string.IsNullOrWhiteSpace(jsonLine))
            {
                return YoutubeGetPlaylistResult.Fail("MCP response did not contain JSON-RPC payload.");
            }

            using var responseDoc = JsonDocument.Parse(jsonLine);
            var root = responseDoc.RootElement;

            if (root.TryGetProperty("error", out var errorNode))
            {
                return YoutubeGetPlaylistResult.Fail(errorNode.GetRawText());
            }

            if (root.TryGetProperty("result", out var resultNode))
            {
                if (resultNode.TryGetProperty("data", out var dataNode))
                {
                    resultNode = dataNode;
                }

                var id = TryGetStringProperty(resultNode, "playlist_id");
                if (string.IsNullOrWhiteSpace(id))
                {
                    return YoutubeGetPlaylistResult.Fail("playlist_id not found in MCP response.");
                }

                return YoutubeGetPlaylistResult.Ok(
                    id,
                    TryGetStringProperty(resultNode, "title"),
                    TryGetStringProperty(resultNode, "privacy"),
                    TryGetStringProperty(resultNode, "url"),
                    TryGetIntProperty(resultNode, "item_count"));
            }

            return YoutubeGetPlaylistResult.Fail("playlist_id not found in MCP response.");
        }
        catch (Exception ex)
        {
            return YoutubeGetPlaylistResult.Fail(ex.Message);
        }
    }

    private static int? TryGetIntProperty(JsonElement root, string propertyName)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var property in root.EnumerateObject())
        {
            if (!property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetInt32(out var value))
            {
                return value;
            }
        }

        return null;
    }

    private async Task UpsertYoutubePlaylistRecordAsync(
        Playlist playlist,
        string youtubePlaylistId,
        YoutubeGetPlaylistResult? lookup,
        string? privacyOverride = null)
    {
        var existing = await _context.YoutubePlaylists
            .FirstOrDefaultAsync(x => x.PlaylistId == playlist.Id);

        if (existing == null)
        {
            existing = new YoutubePlaylist
            {
                Id = Guid.NewGuid(),
                PlaylistId = playlist.Id
            };
            _context.YoutubePlaylists.Add(existing);
        }

        existing.YoutubePlaylistId = youtubePlaylistId;
        existing.Title = lookup?.Title ?? playlist.Title;
        existing.Description = playlist.Description;
        existing.PrivacyStatus = privacyOverride ?? lookup?.Privacy;
        existing.ItemCount = lookup?.ItemCount;
        existing.Status = lookup != null ? "synced" : "created";
        existing.LastSyncedAtUtc = lookup != null ? DateTimeOffset.UtcNow : existing.LastSyncedAtUtc;
        existing.UpdatedAtUtc = DateTimeOffset.UtcNow;
        existing.CreatedAtUtc = existing.CreatedAtUtc == default ? DateTimeOffset.UtcNow : existing.CreatedAtUtc;
    }

    private static string BuildYoutubeUploadThumbnailRequestJson(string videoId, string imagePath)
    {
        var payload = new
        {
            jsonrpc = "2.0",
            id = 12,
            method = "tools/call",
            @params = new
            {
                name = "youtube.upload_thumbnail",
                arguments = new
                {
                    video_id = videoId,
                    image_path = imagePath
                }
            }
        };

        return JsonSerializer.Serialize(payload);
    }

    private async Task<YoutubeSimpleResult> ExecuteYoutubeUploadThumbnailAsync(
        string mcpWorkingDirectory,
        string mcpProject,
        string? allowedRoot,
        string videoId,
        string imagePath)
    {
        try
        {
            var requestJson = BuildYoutubeUploadThumbnailRequestJson(videoId, imagePath);
            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"run --project \"{mcpProject}\"",
                WorkingDirectory = mcpWorkingDirectory,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            if (!string.IsNullOrWhiteSpace(allowedRoot))
            {
                startInfo.Environment["YOUTUBE_ALLOWED_ROOT"] = allowedRoot;
            }

            using var process = new Process { StartInfo = startInfo };
            process.Start();
            await process.StandardInput.WriteLineAsync(requestJson);
            process.StandardInput.Close();
            var stdOut = await process.StandardOutput.ReadToEndAsync();
            var stdErr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                var error = string.IsNullOrWhiteSpace(stdErr) ? $"MCP process exit code {process.ExitCode}" : stdErr.Trim();
                return YoutubeSimpleResult.Fail(error);
            }

            var jsonLine = TryExtractJsonRpcLine(stdOut);
            if (string.IsNullOrWhiteSpace(jsonLine))
            {
                return YoutubeSimpleResult.Fail("MCP response did not contain JSON-RPC payload.");
            }

            using var responseDoc = JsonDocument.Parse(jsonLine);
            var root = responseDoc.RootElement;

            if (root.TryGetProperty("error", out var errorNode))
            {
                return YoutubeSimpleResult.Fail(errorNode.GetRawText());
            }

            return YoutubeSimpleResult.Ok();
        }
        catch (Exception ex)
        {
            return YoutubeSimpleResult.Fail(ex.Message);
        }
    }

    private static string BuildYoutubeAddVideosToPlaylistRequestJson(
        string playlistId,
        IReadOnlyList<string> videoIds)
    {
        var payload = new
        {
            jsonrpc = "2.0",
            id = 14,
            method = "tools/call",
            @params = new
            {
                name = "youtube.add_videos_to_playlist",
                arguments = new
                {
                    playlist_id = playlistId,
                    video_ids = videoIds
                }
            }
        };

        return JsonSerializer.Serialize(payload);
    }

    private async Task<YoutubeAddVideosResult> ExecuteYoutubeAddVideosToPlaylistAsync(
        string mcpWorkingDirectory,
        string mcpProject,
        string playlistId,
        IReadOnlyList<string> videoIds)
    {
        try
        {
            var requestJson = BuildYoutubeAddVideosToPlaylistRequestJson(playlistId, videoIds);
            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"run --project \"{mcpProject}\"",
                WorkingDirectory = mcpWorkingDirectory,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = startInfo };
            process.Start();
            await process.StandardInput.WriteLineAsync(requestJson);
            process.StandardInput.Close();
            var stdOut = await process.StandardOutput.ReadToEndAsync();
            var stdErr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                var error = string.IsNullOrWhiteSpace(stdErr) ? $"MCP process exit code {process.ExitCode}" : stdErr.Trim();
                return YoutubeAddVideosResult.Fail(error);
            }

            var jsonLine = TryExtractJsonRpcLine(stdOut);
            if (string.IsNullOrWhiteSpace(jsonLine))
            {
                return YoutubeAddVideosResult.Fail("MCP response did not contain JSON-RPC payload.");
            }

            using var responseDoc = JsonDocument.Parse(jsonLine);
            var root = responseDoc.RootElement;

            if (root.TryGetProperty("error", out var errorNode))
            {
                return YoutubeAddVideosResult.Fail(errorNode.GetRawText());
            }

            if (root.TryGetProperty("result", out var resultNode))
            {
                if (resultNode.TryGetProperty("data", out var dataNode))
                {
                    resultNode = dataNode;
                }

                var addedCount = 0;
                if (resultNode.TryGetProperty("items_added", out var itemsNode) &&
                    itemsNode.ValueKind == JsonValueKind.Array)
                {
                    addedCount = itemsNode.GetArrayLength();
                }

                return YoutubeAddVideosResult.Ok(addedCount);
            }

            return YoutubeAddVideosResult.Fail("Unexpected MCP response.");
        }
        catch (Exception ex)
        {
            return YoutubeAddVideosResult.Fail(ex.Message);
        }
    }

    private async Task PersistTrackImagesAsync(
        Playlist playlist,
        Track? track,
        int position,
        string prompt,
        string model,
        string aspectRatio,
        ImageGenerationExecutionResult result)
    {
        var targetTrack = track ?? playlist.Tracks.FirstOrDefault(t => t.PlaylistPosition == position);
        if (targetTrack == null)
        {
            return;
        }

        if (result.SavedFiles.Count == 0)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        foreach (var saved in result.SavedFiles)
        {
            var existing = await _context.Set<TrackImage>()
                .AsNoTracking()
                .FirstOrDefaultAsync(x =>
                    x.TrackId == targetTrack.Id &&
                    x.FilePath == saved.FilePath);

            if (existing != null)
            {
                continue;
            }

            var image = new TrackImage
            {
                Id = Guid.NewGuid(),
                TrackId = targetTrack.Id,
                PlaylistId = playlist.Id,
                PlaylistPosition = position,
                FileName = saved.FileName,
                FilePath = saved.FilePath,
                SourceUrl = saved.SourceUrl,
                Model = model,
                Prompt = prompt,
                AspectRatio = aspectRatio,
                CreatedAtUtc = now
            };

            _context.Add(image);
        }

        await _context.SaveChangesAsync();
    }

    private static int ResolveStaleInProgressMinutes()
    {
        var value = Environment.GetEnvironmentVariable("YT_PRODUCER_MEDIA_INPROGRESS_STALE_MINUTES");
        return int.TryParse(value, out var minutes) && minutes > 0 ? minutes : 180;
    }

    private static int ResolveMediaParallelism()
    {
        var value = Environment.GetEnvironmentVariable("YT_PRODUCER_MEDIA_PARALLELISM");
        return int.TryParse(value, out var parallelism) && parallelism > 0 ? parallelism : 3;
    }

    private static PlaylistLockHandle? TryAcquirePlaylistLock(string playlistFolderPath)
    {
        var lockPath = Path.Combine(playlistFolderPath, ".media-generation.lock");
        try
        {
            var stream = new FileStream(lockPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
            return new PlaylistLockHandle(lockPath, stream);
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static void CleanupStalePlaylistLock(string playlistFolderPath, TimeSpan staleThreshold)
    {
        var lockPath = Path.Combine(playlistFolderPath, ".media-generation.lock");
        if (!File.Exists(lockPath))
        {
            return;
        }

        var age = DateTimeOffset.UtcNow - File.GetLastWriteTimeUtc(lockPath);
        if (age <= staleThreshold)
        {
            return;
        }

        try
        {
            File.Delete(lockPath);
        }
        catch
        {
            // Best effort stale lock cleanup.
        }
    }

    private static string GetMediaGenerationStatePath(string playlistFolderPath)
    {
        return Path.Combine(playlistFolderPath, MediaGenerationStateFileName);
    }

    private static MediaGenerationState LoadMediaGenerationState(string playlistFolderPath)
    {
        var statePath = GetMediaGenerationStatePath(playlistFolderPath);
        if (!File.Exists(statePath))
        {
            return new MediaGenerationState();
        }

        try
        {
            var json = File.ReadAllText(statePath);
            var state = JsonSerializer.Deserialize<MediaGenerationState>(json);
            return state ?? new MediaGenerationState();
        }
        catch (Exception)
        {
            return new MediaGenerationState();
        }
    }

    private static void SaveMediaGenerationState(string playlistFolderPath, MediaGenerationState state)
    {
        var statePath = GetMediaGenerationStatePath(playlistFolderPath);
        var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
        var tempPath = $"{statePath}.tmp";
        File.WriteAllText(tempPath, json, new UTF8Encoding(false));
        File.Move(tempPath, statePath, true);
    }

    private static void ExpireStaleInProgressEntries(MediaGenerationState state, TimeSpan staleThreshold)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var key in state.Entries.Keys.ToList())
        {
            var entry = state.Entries[key];
            if (!string.Equals(entry.Status, "in_progress", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var startedAt = entry.LastStartedAtUtc ?? DateTimeOffset.MinValue;
            if (now - startedAt <= staleThreshold)
            {
                continue;
            }

            entry.Status = "failed";
            entry.LastFinishedAtUtc = now;
            entry.LastError = "Expired stale in_progress entry.";
            state.Entries[key] = entry;
        }
    }

    private static void MarkCompletedFromLocalVideo(MediaGenerationState state, RenderCandidate candidate)
    {
        if (!state.Entries.TryGetValue(candidate.Key, out var entry))
        {
            entry = new MediaGenerationEntry();
        }

        entry.Status = "completed";
        entry.AudioPath = candidate.AudioPath;
        entry.ImagePath = candidate.ImagePath;
        entry.VideoPath = candidate.LocalVideoPath;
        entry.LastFinishedAtUtc = DateTimeOffset.UtcNow;
        entry.LastError = null;
        state.Entries[candidate.Key] = entry;
    }

    private static List<RenderCandidate> DiscoverRenderCandidates(string playlistFolderPath)
    {
        var files = Directory.EnumerateFiles(playlistFolderPath)
            .Where(path =>
            {
                var name = Path.GetFileName(path);
                return !string.IsNullOrWhiteSpace(name) && !name.StartsWith("._", StringComparison.Ordinal);
            })
            .ToList();

        var audioByKey = files
            .Where(path => AudioExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            .GroupBy(path => Path.GetFileNameWithoutExtension(path), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).First(), StringComparer.OrdinalIgnoreCase);

        var imageByKey = files
            .Where(path => ImageExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            .GroupBy(path => Path.GetFileNameWithoutExtension(path), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).First(), StringComparer.OrdinalIgnoreCase);

        var candidates = new List<RenderCandidate>();
        foreach (var key in audioByKey.Keys.Intersect(imageByKey.Keys, StringComparer.OrdinalIgnoreCase))
        {
            candidates.Add(new RenderCandidate(
                Key: key,
                ImagePath: imageByKey[key],
                AudioPath: audioByKey[key],
                LocalVideoPath: Path.Combine(playlistFolderPath, $"{key}.mp4")));
        }

        return candidates
            .OrderBy(c => ParseMediaSortKey(c.Key).Position)
            .ThenBy(c => ParseMediaSortKey(c.Key).Variant)
            .ThenBy(c => c.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static (int Position, int Variant) ParseMediaSortKey(string key)
    {
        var match = Regex.Match(key, "^(?<position>\\d+)(?:_(?<variant>\\d+))?$", RegexOptions.CultureInvariant);
        if (!match.Success)
        {
            return (int.MaxValue, int.MaxValue);
        }

        var position = int.Parse(match.Groups["position"].Value);
        var variant = match.Groups["variant"].Success ? int.Parse(match.Groups["variant"].Value) : 0;
        return (position, variant);
    }

    private async Task<YoutubeLastPublishedDate> GetOrCreateYoutubeLastPublishedDateAsync()
    {
        var state = await _context.YoutubeLastPublishedDates
            .FirstOrDefaultAsync(x => x.Id == 1);

        if (state != null)
        {
            return state;
        }

        state = new YoutubeLastPublishedDate
        {
            Id = 1,
            LastPublishedDate = new DateTimeOffset(2026, 3, 8, 8, 0, 0, TimeSpan.Zero),
            VideoId = null
        };
        _context.Add(state);
        await _context.SaveChangesAsync();
        return state;
    }

    private static DateTimeOffset GetNextYoutubePublishSlotUtc(DateTimeOffset lastPublishedDate)
    {
        var utc = lastPublishedDate.ToUniversalTime();
        var date = utc.Date;

        foreach (var hour in YoutubePublishHoursUtc.OrderBy(h => h))
        {
            var candidate = new DateTimeOffset(date.Year, date.Month, date.Day, hour, 0, 0, TimeSpan.Zero);
            if (candidate > utc)
            {
                return candidate;
            }
        }

        var nextDay = date.AddDays(1);
        var firstHour = YoutubePublishHoursUtc.Min();
        return new DateTimeOffset(nextDay.Year, nextDay.Month, nextDay.Day, firstHour, 0, 0, TimeSpan.Zero);
    }

    private static string BuildVisualizerRequestJsonWithDirs(
        string imagePath,
        string audioPath,
        string tempDir,
        string outputDir)
    {
        var payload = new
        {
            jsonrpc = "2.0",
            id = 10,
            method = "tools/call",
            @params = new
            {
                name = "video.create_music_visualizer",
                arguments = new
                {
                    image_path = imagePath,
                    audio_path = audioPath,
                    width = 1920,
                    height = 1080,
                    fps = 30,
                    eq_bands = 64,
                    video_bitrate = "12M",
                    audio_bitrate = "320k",
                    keep_temp = false,
                    temp_dir = tempDir,
                    output_dir = outputDir
                }
            }
        };

        return JsonSerializer.Serialize(payload);
    }

    private static string BuildCreateYoutubeVideoThumbnailRequestJson(
        string toolName,
        string imagePath,
        string outputPath,
        string? logoPath,
        string headline,
        string subheadline,
        string? headlineFont,
        string? subheadlineFont,
        string? headlineColor,
        string? subheadlineColor)
    {
        var payload = new
        {
            jsonrpc = "2.0",
            id = 15,
            method = "tools/call",
            @params = new
            {
                name = toolName,
                arguments = new
                {
                    image_path = imagePath,
                    logo_path = string.IsNullOrWhiteSpace(logoPath) ? null : logoPath,
                    headline,
                    subheadline,
                    output_path = outputPath,
                    style = new
                    {
                        headline_font = string.IsNullOrWhiteSpace(headlineFont) ? null : headlineFont,
                        subheadline_font = string.IsNullOrWhiteSpace(subheadlineFont) ? null : subheadlineFont,
                        headline_color = string.IsNullOrWhiteSpace(headlineColor) ? null : headlineColor,
                        subheadline_color = string.IsNullOrWhiteSpace(subheadlineColor) ? null : subheadlineColor,
                        shadow = true,
                        stroke = true
                    }
                }
            }
        };

        return JsonSerializer.Serialize(payload);
    }

    private static string? TryExtractJsonRpcLine(string stdout)
    {
        var lines = stdout
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        for (var i = lines.Length - 1; i >= 0; i--)
        {
            var line = lines[i];
            if (!line.StartsWith("{", StringComparison.Ordinal) || !line.Contains("\"jsonrpc\"", StringComparison.Ordinal))
            {
                continue;
            }

            return line;
        }

        return null;
    }

    private static string? ResolveVideoPathForPosition(string playlistFolderPath, int position)
    {
        var preferred = Path.Combine(playlistFolderPath, $"{position}.mp4");
        if (File.Exists(preferred))
        {
            return preferred;
        }

        var matches = Directory.EnumerateFiles(playlistFolderPath, $"{position}_*.mp4")
            .Where(path =>
            {
                var name = Path.GetFileName(path);
                return !string.IsNullOrWhiteSpace(name) && !name.StartsWith("._", StringComparison.Ordinal);
            })
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return matches.FirstOrDefault();
    }

    private static string? ResolveImagePathForPosition(string playlistFolderPath, int position)
    {
        var preferred = ImageExtensions
            .Select(ext => Path.Combine(playlistFolderPath, $"{position}{ext}"))
            .FirstOrDefault(File.Exists);

        if (!string.IsNullOrWhiteSpace(preferred))
        {
            return preferred;
        }

        var matches = Directory.EnumerateFiles(playlistFolderPath)
            .Where(path =>
            {
                var name = Path.GetFileName(path);
                if (string.IsNullOrWhiteSpace(name) || name.StartsWith("._", StringComparison.Ordinal))
                {
                    return false;
                }

                var extension = Path.GetExtension(name);
                if (!ImageExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
                {
                    return false;
                }

                return TryMatchPosition(name, position);
            })
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return matches.FirstOrDefault();
    }

    private static string? ResolveYoutubeThumbnailPathForPosition(string playlistFolderPath, int position)
    {
        var preferred = ImageExtensions
            .Select(ext => Path.Combine(playlistFolderPath, $"{position}_thumbnail{ext}"))
            .FirstOrDefault(File.Exists);

        if (!string.IsNullOrWhiteSpace(preferred))
        {
            return preferred;
        }

        var matches = Directory.EnumerateFiles(playlistFolderPath)
            .Where(path =>
            {
                var name = Path.GetFileName(path);
                if (string.IsNullOrWhiteSpace(name) || name.StartsWith("._", StringComparison.Ordinal))
                {
                    return false;
                }

                var extension = Path.GetExtension(name);
                if (!ImageExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
                {
                    return false;
                }

                var baseName = Path.GetFileNameWithoutExtension(name);
                return string.Equals(baseName, $"{position}_thumbnail", StringComparison.OrdinalIgnoreCase)
                    || baseName.StartsWith($"{position}_thumbnail_", StringComparison.OrdinalIgnoreCase);
            })
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return matches.FirstOrDefault();
    }

    private static string? ExtractLikelyOutputPath(JsonElement resultElement)
    {
        if (resultElement.ValueKind == JsonValueKind.Object)
        {
            var preferredPropertyNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "output_path",
                "outputPath",
                "video_path",
                "videoPath",
                "path",
                "file_path",
                "filePath"
            };

            foreach (var property in resultElement.EnumerateObject())
            {
                if (preferredPropertyNames.Contains(property.Name)
                    && property.Value.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(property.Value.GetString()))
                {
                    return property.Value.GetString();
                }
            }

            foreach (var property in resultElement.EnumerateObject())
            {
                var nested = ExtractLikelyOutputPath(property.Value);
                if (!string.IsNullOrWhiteSpace(nested))
                {
                    return nested;
                }
            }
        }
        else if (resultElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in resultElement.EnumerateArray())
            {
                var nested = ExtractLikelyOutputPath(item);
                if (!string.IsNullOrWhiteSpace(nested))
                {
                    return nested;
                }
            }
        }

        return null;
    }

    private async Task<MediaExecutionResult> ExecuteVisualizerRenderAsync(
        string mcpMediaWorkingDirectory,
        string mcpMediaProject,
        string tempDir,
        string outputDir,
        string imagePath,
        string audioPath)
    {
        try
        {
            var requestJson = BuildVisualizerRequestJsonWithDirs(imagePath, audioPath, tempDir, outputDir);

            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"run --project \"{mcpMediaProject}\"",
                WorkingDirectory = mcpMediaWorkingDirectory,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.Environment["MEDIA_TMP_DIR"] = tempDir;
            startInfo.Environment["MEDIA_OUTPUT_DIR"] = outputDir;

            using var process = new Process { StartInfo = startInfo };
            process.Start();
            await process.StandardInput.WriteLineAsync(requestJson);
            process.StandardInput.Close();
            var stdOut = await process.StandardOutput.ReadToEndAsync();
            var stdErr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                var error = string.IsNullOrWhiteSpace(stdErr) ? $"MCP process exit code {process.ExitCode}" : stdErr.Trim();
                return MediaExecutionResult.Fail(error);
            }

            var jsonLine = TryExtractJsonRpcLine(stdOut);
            if (string.IsNullOrWhiteSpace(jsonLine))
            {
                return MediaExecutionResult.Fail("MCP response did not contain JSON-RPC payload.");
            }

            using var responseDoc = JsonDocument.Parse(jsonLine);
            var root = responseDoc.RootElement;

            if (root.TryGetProperty("error", out var errorNode))
            {
                return MediaExecutionResult.Fail(errorNode.GetRawText());
            }

            string? outputPath = null;
            if (root.TryGetProperty("result", out var resultNode))
            {
                outputPath = ExtractLikelyOutputPath(resultNode);
            }

            return MediaExecutionResult.Ok(outputPath);
        }
        catch (Exception ex)
        {
            return MediaExecutionResult.Fail(ex.Message);
        }
    }

    private async Task<MediaExecutionResult> ExecuteCreateYoutubeVideoThumbnailAsync(
        string mcpMediaWorkingDirectory,
        string mcpMediaProject,
        string toolName,
        string imagePath,
        string outputPath,
        string? logoPath,
        string headline,
        string subheadline,
        string? headlineFont,
        string? subheadlineFont,
        string? headlineColor,
        string? subheadlineColor)
    {
        try
        {
            var requestJson = BuildCreateYoutubeVideoThumbnailRequestJson(
                toolName,
                imagePath,
                outputPath,
                logoPath,
                headline,
                subheadline,
                headlineFont,
                subheadlineFont,
                headlineColor,
                subheadlineColor);

            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"run --project \"{mcpMediaProject}\"",
                WorkingDirectory = mcpMediaWorkingDirectory,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.Environment["MEDIA_TMP_DIR"] = mcpMediaWorkingDirectory;
            startInfo.Environment["MEDIA_OUTPUT_DIR"] = Path.GetDirectoryName(outputPath) ?? mcpMediaWorkingDirectory;

            using var process = new Process { StartInfo = startInfo };
            process.Start();
            await process.StandardInput.WriteLineAsync(requestJson);
            process.StandardInput.Close();
            var stdOut = await process.StandardOutput.ReadToEndAsync();
            var stdErr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                var error = string.IsNullOrWhiteSpace(stdErr) ? $"MCP process exit code {process.ExitCode}" : stdErr.Trim();
                return MediaExecutionResult.Fail(error);
            }

            var jsonLine = TryExtractJsonRpcLine(stdOut);
            if (string.IsNullOrWhiteSpace(jsonLine))
            {
                return File.Exists(outputPath)
                    ? MediaExecutionResult.Ok(outputPath)
                    : MediaExecutionResult.Fail("MCP response did not contain JSON-RPC payload.");
            }

            using var responseDoc = JsonDocument.Parse(jsonLine);
            var root = responseDoc.RootElement;

            if (root.TryGetProperty("error", out var errorNode))
            {
                return MediaExecutionResult.Fail(errorNode.GetRawText());
            }

            string? resolvedOutputPath = null;
            if (root.TryGetProperty("result", out var resultNode))
            {
                resolvedOutputPath = ExtractLikelyOutputPath(resultNode);
            }

            if (string.IsNullOrWhiteSpace(resolvedOutputPath))
            {
                resolvedOutputPath = File.Exists(outputPath) ? outputPath : null;
            }

            if (string.IsNullOrWhiteSpace(resolvedOutputPath))
            {
                return MediaExecutionResult.Fail("Thumbnail output file was not produced.");
            }

            return MediaExecutionResult.Ok(resolvedOutputPath);
        }
        catch (Exception ex)
        {
            return MediaExecutionResult.Fail(ex.Message);
        }
    }

    private static bool HasMediaForPosition(IEnumerable<string> fileNames, int position, IReadOnlyCollection<string> allowedExtensions)
    {
        var pattern = $"^{position}(?:_\\d+)?(?:{string.Join("|", allowedExtensions.Select(Regex.Escape))})$";
        return fileNames.Any(name => Regex.IsMatch(name, pattern, RegexOptions.IgnoreCase));
    }

    private async Task<Dictionary<int, TrackPromptData>> GetTrackPromptsByPositionAsync(Guid playlistId)
    {
        var tracks = await _context.Tracks
            .AsNoTracking()
            .Where(t => t.PlaylistId == playlistId)
            .OrderBy(t => t.PlaylistPosition)
            .ThenBy(t => t.CreatedAtUtc)
            .Select(t => new { t.PlaylistPosition, t.Title, t.Metadata })
            .ToListAsync();

        var promptsByPosition = new Dictionary<int, TrackPromptData>();
        foreach (var track in tracks)
        {
            if (!promptsByPosition.TryGetValue(track.PlaylistPosition, out var existing))
            {
                existing = new TrackPromptData(track.Title, null, null);
            }

            var parsed = ParsePromptMetadata(track.Metadata);
            var merged = new TrackPromptData(
                string.IsNullOrWhiteSpace(existing.TrackTitle) ? track.Title : existing.TrackTitle,
                string.IsNullOrWhiteSpace(existing.ImagePrompt) ? parsed.ImagePrompt : existing.ImagePrompt,
                string.IsNullOrWhiteSpace(existing.SunoPrompt) ? parsed.SunoPrompt : existing.SunoPrompt);

            promptsByPosition[track.PlaylistPosition] = merged;
        }

        return promptsByPosition;
    }

    private static PromptMetadata ParsePromptMetadata(string? metadata)
    {
        if (string.IsNullOrWhiteSpace(metadata))
        {
            return new PromptMetadata(null, null);
        }

        try
        {
            using var document = JsonDocument.Parse(metadata);
            var root = document.RootElement;
            var imagePrompt = TryGetStringProperty(root, "imagePrompt");
            var sunoPrompt = TryGetStringProperty(root, "musicGenerationPrompt");
            return new PromptMetadata(imagePrompt, sunoPrompt);
        }
        catch (JsonException)
        {
            return new PromptMetadata(null, null);
        }
    }

    private static string? TryGetStringProperty(JsonElement root, string propertyName)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var property in root.EnumerateObject())
        {
            if (!property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (property.Value.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            var value = property.Value.GetString();
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        return null;
    }

    private static string? ResolveYoutubeDescription(Track track, Playlist playlist)
    {
        var trackMetadata = track.Metadata;
        var playlistMetadata = playlist.Metadata;
        var fallbackDescription = playlist.Description;

        var metadataDescription = TryGetMetadataString(trackMetadata, "youtubeDescription")
            ?? TryGetMetadataString(playlistMetadata, "youtubeDescription");

        var scenario = FirstNonEmpty(
            TryGetMetadataString(trackMetadata, "listeningScenario"),
            TryGetMetadataString(trackMetadata, "playlistCategory"),
            TryGetMetadataString(playlistMetadata, "playlistCategory"),
            "workout");
        var hookType = TryGetMetadataString(trackMetadata, "hookType");
        var energyCurve = TryGetMetadataString(trackMetadata, "energyCurve");
        var musicPrompt = TryGetMetadataString(trackMetadata, "musicGenerationPrompt")
            ?? TryGetMetadataString(playlistMetadata, "musicGenerationPrompt");
        var genre = TryExtractFieldFromPrompt(musicPrompt, "Genre");
        var subgenre = TryExtractFieldFromPrompt(musicPrompt, "Subgenre");
        var bpm = track.TempoBpm?.ToString() ?? TryExtractFieldFromPrompt(musicPrompt, "BPM");
        var musicalKey = track.Key ?? TryExtractFieldFromPrompt(musicPrompt, "Key");
        var vibe = FirstNonEmpty(
            track.Style,
            TryGetMetadataString(trackMetadata, "thumbnailEmotion"),
            TryGetMetadataString(trackMetadata, "targetAudience"),
            hookType,
            energyCurve,
            "Focused, Steady, Driving");

        var brandName = Environment.GetEnvironmentVariable("YT_PRODUCER_BRAND_NAME")?.Trim();
        if (string.IsNullOrWhiteSpace(brandName))
        {
            brandName = "AuruZ Music";
        }

        var subscribeLink = Environment.GetEnvironmentVariable("YT_PRODUCER_YOUTUBE_SUBSCRIBE_LINK")?.Trim();
        if (string.IsNullOrWhiteSpace(subscribeLink))
        {
            subscribeLink = "[Link]";
        }

        var playlistLink = Environment.GetEnvironmentVariable("YT_PRODUCER_YOUTUBE_PLAYLIST_LINK")?.Trim();
        if (string.IsNullOrWhiteSpace(playlistLink))
        {
            playlistLink = "[Link]";
        }

        var effectiveGenre = FirstNonEmpty(subgenre, genre, "Electronic Workout");
        var details = new List<string>
        {
            $"Artist: {brandName}",
            $"Genre: {effectiveGenre} / Instrumental Gym Music",
            $"Vibe: {vibe}"
        };

        if (!string.IsNullOrWhiteSpace(bpm))
        {
            details.Add($"Tempo: {bpm.Trim()} BPM");
        }

        if (!string.IsNullOrWhiteSpace(musicalKey))
        {
            details.Add($"Key: {musicalKey.Trim()}");
        }

        if (track.EnergyLevel.HasValue)
        {
            details.Add($"Energy: {track.EnergyLevel.Value}/10");
        }

        var lines = new List<string>
        {
            $"Elevate your {scenario.Trim()} ritual with {brandName}. ⚡",
            string.Empty,
            $"This {effectiveGenre} track is built to lock your focus and move you into a high-performance state.",
            $"Whether you're preparing for heavy lifting or cardio, this rhythm helps keep you in the zone."
        };

        if (!string.IsNullOrWhiteSpace(metadataDescription))
        {
            lines.Add(string.Empty);
            lines.Add(metadataDescription.Trim());
        }

        lines.Add(string.Empty);
        lines.Add("🎵 Track Details:");
        lines.Add(string.Empty);
        lines.AddRange(details);
        lines.Add(string.Empty);
        lines.Add($"👇 Support {brandName}:");
        lines.Add(string.Empty);
        lines.Add($"Subscribe for weekly gym fuel: {subscribeLink}");
        lines.Add($"Save this playlist: {playlistLink}");

        var tags = TryGetMetadataStringArray(trackMetadata, "youtubeTags");
        if (tags.Count == 0)
        {
            tags = TryGetMetadataStringArray(playlistMetadata, "youtubeTags");
        }

        var hashTagLine = BuildHashtagLine(tags);
        if (!string.IsNullOrWhiteSpace(hashTagLine))
        {
            lines.Add(string.Empty);
            lines.Add(hashTagLine);
        }

        var description = string.Join(Environment.NewLine, lines).Trim();
        return string.IsNullOrWhiteSpace(description) ? fallbackDescription : description;
    }

    private static string? TryGetMetadataString(string? metadata, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(metadata))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(metadata);
            return TryGetStringProperty(document.RootElement, propertyName);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static IReadOnlyList<string> TryGetMetadataStringArray(string? metadata, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(metadata))
        {
            return Array.Empty<string>();
        }

        try
        {
            using var document = JsonDocument.Parse(metadata);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return Array.Empty<string>();
            }

            foreach (var property in root.EnumerateObject())
            {
                if (!property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (property.Value.ValueKind != JsonValueKind.Array)
                {
                    return Array.Empty<string>();
                }

                var list = new List<string>();
                foreach (var item in property.Value.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        var value = item.GetString();
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            list.Add(value.Trim());
                        }
                    }
                }

                return list;
            }

            return Array.Empty<string>();
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }

    private static string BuildHashtagLine(IReadOnlyList<string> tags)
    {
        if (tags.Count == 0)
        {
            return string.Empty;
        }

        var tagSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tag in tags)
        {
            var normalized = NormalizeTag(tag);
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                tagSet.Add(normalized);
            }
        }

        if (tagSet.Count == 0)
        {
            return string.Empty;
        }

        return string.Join(" ", tagSet.Take(12).Select(tag => $"#{tag}"));
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return string.Empty;
    }

    private static string NormalizeTag(string tag)
    {
        var trimmed = tag.Trim();
        if (trimmed.StartsWith("#"))
        {
            trimmed = trimmed[1..];
        }

        return string.IsNullOrWhiteSpace(trimmed) ? string.Empty : trimmed.Replace(" ", string.Empty);
    }

    private void WriteImagePromptFile(
        string playlistFolderPath,
        string? playlistTitle,
        string? playlistTheme,
        IReadOnlyCollection<int> missingPositions,
        IReadOnlyDictionary<int, TrackPromptData> trackPromptsByPosition,
        string? playlistLevelImagePrompt)
    {
        var promptFilePath = Path.Combine(playlistFolderPath, "missing_image_prompts.txt");

        if (missingPositions.Count == 0)
        {
            if (File.Exists(promptFilePath))
            {
                File.Delete(promptFilePath);
            }

            return;
        }

        var safeTitle = string.IsNullOrWhiteSpace(playlistTitle) ? "Untitled playlist" : playlistTitle.Trim();
        var safeTheme = string.IsNullOrWhiteSpace(playlistTheme) ? "cinematic electronic" : playlistTheme.Trim();
        var lines = new List<string>
        {
            $"# Missing image prompts for {safeTitle}",
            $"# Generated at UTC: {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss}",
            ""
        };

        lines.AddRange(missingPositions.Select(position =>
        {
            var metadataPrompt = trackPromptsByPosition.TryGetValue(position, out var trackData)
                ? trackData.ImagePrompt
                : null;
            var prompt = metadataPrompt
                ?? playlistLevelImagePrompt
                ?? $"Square cover art for track {position} in playlist \"{safeTitle}\". Theme: {safeTheme}. Bold composition, clean typography space, professional YouTube quality.";
            return $"{position}: {prompt}";
        }));

        WriteTextFileIfChanged(promptFilePath, lines);
    }

    private void WriteSunoPromptFile(
        string playlistFolderPath,
        string? playlistTitle,
        string? playlistTheme,
        IReadOnlyCollection<int> missingPositions,
        IReadOnlyDictionary<int, TrackPromptData> trackPromptsByPosition,
        string? playlistLevelSunoPrompt)
    {
        var promptFilePath = Path.Combine(playlistFolderPath, "missing_suno_prompts.txt");

        if (missingPositions.Count == 0)
        {
            if (File.Exists(promptFilePath))
            {
                File.Delete(promptFilePath);
            }

            return;
        }

        var safeTitle = string.IsNullOrWhiteSpace(playlistTitle) ? "Untitled playlist" : playlistTitle.Trim();
        var safeTheme = string.IsNullOrWhiteSpace(playlistTheme) ? "cinematic electronic" : playlistTheme.Trim();
        var lines = new List<string>
        {
            $"# Missing Suno prompts for {safeTitle}",
            $"# Generated at UTC: {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss}",
            ""
        };

        lines.AddRange(missingPositions.Select(position =>
        {
            var metadataPrompt = trackPromptsByPosition.TryGetValue(position, out var trackData)
                ? trackData.SunoPrompt
                : null;
            var prompt = metadataPrompt
                ?? playlistLevelSunoPrompt
                ?? $"Create track {position} for playlist \"{safeTitle}\". Theme: {safeTheme}. Instrumental, 3-4 minutes, strong hook, modern production, no vocals.";
            return $"{position}: {prompt}";
        }));

        WriteTextFileIfChanged(promptFilePath, lines);
    }

    private static void WriteTextFileIfChanged(string filePath, IReadOnlyCollection<string> lines)
    {
        var newContent = string.Join(Environment.NewLine, lines) + Environment.NewLine;
        if (File.Exists(filePath))
        {
            var existingContent = File.ReadAllText(filePath);
            if (string.Equals(existingContent, newContent, StringComparison.Ordinal))
            {
                return;
            }
        }

        File.WriteAllText(filePath, newContent, new UTF8Encoding(false));
    }

    private sealed class PlaylistLockHandle : IDisposable
    {
        private readonly string _lockPath;
        private readonly FileStream _stream;

        public PlaylistLockHandle(string lockPath, FileStream stream)
        {
            _lockPath = lockPath;
            _stream = stream;
        }

        public void Dispose()
        {
            _stream.Dispose();
            try
            {
                if (File.Exists(_lockPath))
                {
                    File.Delete(_lockPath);
                }
            }
            catch
            {
                // Best effort cleanup.
            }
        }
    }

    private sealed class MediaGenerationState
    {
        public Dictionary<string, MediaGenerationEntry> Entries { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class MediaGenerationEntry
    {
        public string Status { get; set; } = "pending";
        public int Attempts { get; set; }
        public string? ImagePath { get; set; }
        public string? AudioPath { get; set; }
        public string? VideoPath { get; set; }
        public DateTimeOffset? LastStartedAtUtc { get; set; }
        public DateTimeOffset? LastFinishedAtUtc { get; set; }
        public string? LastError { get; set; }
    }

    private sealed class MediaGenerationSummary
    {
        public int PlaylistsScanned { get; set; }
        public int PlaylistsLocked { get; set; }
        public int CandidatesFound { get; set; }
        public int Scheduled { get; set; }
        public int Failed { get; set; }
        public int SkippedInProgress { get; set; }
        public int SkippedCompleted { get; set; }
    }

    private sealed record RenderCandidate(string Key, string ImagePath, string AudioPath, string? LocalVideoPath);

    private sealed record MediaExecutionResult(bool Success, string? OutputPath, string? ErrorMessage)
    {
        public static MediaExecutionResult Ok(string? outputPath) => new(true, outputPath, null);
        public static MediaExecutionResult Fail(string errorMessage) => new(false, null, errorMessage);
    }

    private sealed record SavedImageArtifact(string FilePath, string FileName, string? SourceUrl);

    private sealed record ImageGenerationExecutionResult(bool Success, IReadOnlyList<SavedImageArtifact> SavedFiles, string? ErrorMessage)
    {
        public static ImageGenerationExecutionResult Ok(IReadOnlyList<SavedImageArtifact> savedFiles) => new(true, savedFiles, null);
        public static ImageGenerationExecutionResult Fail(string errorMessage) => new(false, Array.Empty<SavedImageArtifact>(), errorMessage);
    }

    private sealed record YoutubePlaylistCreateResult(bool Success, string? PlaylistId, string? ErrorMessage)
    {
        public static YoutubePlaylistCreateResult Ok(string playlistId) => new(true, playlistId, null);
        public static YoutubePlaylistCreateResult Fail(string errorMessage) => new(false, null, errorMessage);
    }

    private sealed record YoutubeUploadVideoResult(bool Success, string? VideoId, string? Url, string? ErrorMessage)
    {
        public static YoutubeUploadVideoResult Ok(string videoId, string? url) => new(true, videoId, url, null);
        public static YoutubeUploadVideoResult Fail(string errorMessage) => new(false, null, null, errorMessage);
    }

    private sealed record YoutubeGetPlaylistResult(
        bool Success,
        string? PlaylistId,
        string? Title,
        string? Privacy,
        string? Url,
        int? ItemCount,
        string? ErrorMessage)
    {
        public static YoutubeGetPlaylistResult Ok(
            string playlistId,
            string? title,
            string? privacy,
            string? url,
            int? itemCount) => new(true, playlistId, title, privacy, url, itemCount, null);

        public static YoutubeGetPlaylistResult Fail(string errorMessage) => new(false, null, null, null, null, null, errorMessage);
    }

    private sealed record YoutubeSimpleResult(bool Success, string? ErrorMessage)
    {
        public static YoutubeSimpleResult Ok() => new(true, null);
        public static YoutubeSimpleResult Fail(string errorMessage) => new(false, errorMessage);
    }

    private sealed record YoutubeAddVideosResult(bool Success, int AddedCount, string? ErrorMessage)
    {
        public static YoutubeAddVideosResult Ok(int addedCount) => new(true, addedCount, null);
        public static YoutubeAddVideosResult Fail(string errorMessage) => new(false, 0, errorMessage);
    }

    private sealed record PromptMetadata(string? ImagePrompt, string? SunoPrompt);

    private sealed record TrackPromptData(string? TrackTitle, string? ImagePrompt, string? SunoPrompt);

    private sealed class PipelineStats
    {
        public int PlaylistsProcessed { get; set; }
        public int CreatedFolders { get; set; }
        public int ExistingFolders { get; set; }
        public int TotalMissingImages { get; set; }
        public int TotalMissingTracks { get; set; }
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
