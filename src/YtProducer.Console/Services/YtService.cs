using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Diagnostics;
using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SkiaSharp;
using YtProducer.Contracts.Jobs;
using YtProducer.Contracts.Playlists;
using YtProducer.Contracts.YoutubePlaylists;
using YtProducer.Contracts.YoutubeUploadQueue;
using YtProducer.Domain.Entities;
using YtProducer.Domain.Enums;
using YtProducer.Infrastructure.Persistence;
using YtProducer.Media.Models;
using YtProducer.Media.Services;
using YtProducer.ReasoningAI;
using YtProducer.ReasoningAI.Abstractions;

namespace YtProducer.Console.Services;

/// <summary>
/// Working service that combines database operations with API testing.
/// Loads existing records and demonstrates API operations.
/// </summary>
public class YtService
{
    private static readonly string[] ImageExtensions = [".jpg", ".jpeg", ".png", ".webp"];
    private static readonly string[] AudioExtensions = [".mp3"];
    private static readonly (int Hour, int Minute)[] YoutubePublishSlotsUtc =
    [
        (7, 30),
        (11, 0),
        (14, 30),
        (18, 0),
        (21, 30)
    ];
    private const string MediaGenerationStateFileName = ".media-generation-state.json";

    private readonly YtProducerDbContext _context;
    private readonly ApiClient _apiClient;
    private readonly ILogger<YtService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly YoutubeSeoService _youtubeSeoService;
    private readonly IReasoningClientFactory _reasoningClientFactory;

    public YtService(
        YtProducerDbContext context,
        ApiClient apiClient,
        ILogger<YtService> logger,
        IServiceScopeFactory scopeFactory,
        YoutubeSeoService youtubeSeoService,
        IReasoningClientFactory reasoningClientFactory)
    {
        _context = context;
        _apiClient = apiClient;
        _logger = logger;
        _scopeFactory = scopeFactory;
        _youtubeSeoService = youtubeSeoService;
        _reasoningClientFactory = reasoningClientFactory;
    }

    public async Task RunPlaylistInitAsync(string[] commandArgs)
    {
        Guid? targetPlaylistId = null;
        if (commandArgs.Length > 0)
        {
            if (!Guid.TryParse(commandArgs[0], out var parsedPlaylistId))
            {
                global::System.Console.WriteLine("Usage: playlist_init [playlistId]");
                return;
            }

            targetPlaylistId = parsedPlaylistId;
        }

        _logger.LogInformation("╔════════════════════════════════════════════════════╗");
        _logger.LogInformation("║       YtProducer Console - Playlist Pipeline        ║");
        _logger.LogInformation("╚════════════════════════════════════════════════════╝\n");

        if (!targetPlaylistId.HasValue)
        {
            await PrintAllPlaylistsAsync();
            _logger.LogInformation("");
            await PrintAllYoutubePlaylistsAsync();
            _logger.LogInformation("");
        }

        await RunPlaylistPipelineAsync(targetPlaylistId);

        _logger.LogInformation("\n✓ Playlist pipeline completed!");
    }

    public async Task RunProcessJobAsync(string[] commandArgs)
    {
        if (commandArgs.Length != 1 || !Guid.TryParse(commandArgs[0], out var jobId))
        {
            global::System.Console.WriteLine("Usage: process-job <jobId>");
            return;
        }

        var job = await _context.Jobs.FirstOrDefaultAsync(x => x.Id == jobId);
        if (job == null)
        {
            global::System.Console.WriteLine($"Job not found: {jobId}");
            return;
        }

        await ProcessJobInternalAsync(job);
    }

    public async Task RunProcessAllJobsAsync(string[] commandArgs)
    {
        if (commandArgs.Length != 0)
        {
            global::System.Console.WriteLine("Usage: process-all-jobs");
            return;
        }

        var completed = 0;
        var failed = 0;
        var idleDelay = TimeSpan.FromSeconds(5);

        global::System.Console.WriteLine("process-all-jobs worker started");

        while (true)
        {
            _context.ChangeTracker.Clear();
            await RecoverExpiredConsoleJobLeasesAsync();

            var pendingJobIds = await _context.Jobs
                .AsNoTracking()
                .Where(x => x.Status == JobStatus.Pending)
                .OrderBy(x => x.CreatedAt)
                .Select(x => x.Id)
                .ToListAsync();

            if (pendingJobIds.Count == 0)
            {
                await Task.Delay(idleDelay);
                continue;
            }

            foreach (var jobId in pendingJobIds)
            {
                _context.ChangeTracker.Clear();

                var job = await _context.Jobs.FirstOrDefaultAsync(x => x.Id == jobId);
                if (job == null || job.Status != JobStatus.Pending)
                {
                    continue;
                }

                var success = await ProcessJobInternalAsync(job);
                if (success)
                {
                    completed++;
                }
                else
                {
                    failed++;
                }
            }

            global::System.Console.WriteLine(
                $"process-all-jobs heartbeat completed={completed} failed={failed}");
        }
    }

    private async Task<bool> ProcessJobInternalAsync(Job job)
    {
        var workerId = $"console-{Environment.MachineName}-{Environment.ProcessId}";
        var leaseDuration = TimeSpan.FromMinutes(10);

        if (job.Status is JobStatus.Completed or JobStatus.Running)
        {
            global::System.Console.WriteLine($"Job {job.Id} is already {job.Status}.");
            return false;
        }

        job.Status = JobStatus.Running;
        job.WorkerId = workerId;
        job.StartedAt ??= DateTimeOffset.UtcNow;
        job.LastHeartbeat = DateTimeOffset.UtcNow;
        job.LeaseExpiresAt = DateTimeOffset.UtcNow.Add(leaseDuration);
        job.ErrorCode = null;
        job.ErrorMessage = null;
        job.Progress = 0;
        await _context.SaveChangesAsync();

        await AppendJobLogAsync(job.Id, "Info", $"Job started type={job.Type} worker={workerId}");

        using var heartbeatCts = new CancellationTokenSource();
        var heartbeatTask = Task.Run(async () =>
        {
            while (!heartbeatCts.Token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(15), heartbeatCts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                try
                {
                    await using var scope = _scopeFactory.CreateAsyncScope();
                    var scopedContext = scope.ServiceProvider.GetRequiredService<YtProducerDbContext>();
                    var heartbeatAt = DateTimeOffset.UtcNow;
                    var leaseExpiresAt = heartbeatAt.Add(leaseDuration);
                    var affected = await scopedContext.Jobs
                        .Where(x => x.Id == job.Id && x.Status == JobStatus.Running && x.WorkerId == workerId)
                        .ExecuteUpdateAsync(setters => setters
                            .SetProperty(x => x.LastHeartbeat, heartbeatAt)
                            .SetProperty(x => x.LeaseExpiresAt, leaseExpiresAt)
                            .SetProperty(x => x.Progress, job.Progress), heartbeatCts.Token);

                    if (affected == 0)
                    {
                        break;
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    global::System.Console.WriteLine($"job heartbeat failed id={job.Id} error={ex.Message}");
                }
            }
        }, heartbeatCts.Token);

        try
        {
            var result = await ProcessScheduledJobAsync(job);
            await ThrowIfJobCancelledAsync(job.Id);

            job.Status = JobStatus.Completed;
            job.Progress = 100;
            job.ResultJson = result;
            job.FinishedAt = DateTimeOffset.UtcNow;
            job.LastHeartbeat = job.FinishedAt;
            job.LeaseExpiresAt = null;
            await _context.SaveChangesAsync();

            await AppendJobLogAsync(job.Id, "Info", "Job completed successfully.", result);
            global::System.Console.WriteLine($"process-job complete id={job.Id} type={job.Type}");
            return true;
        }
        catch (OperationCanceledException ex)
        {
            if (await IsJobCancelledAsync(job.Id))
            {
                _context.ChangeTracker.Clear();

                var cancelledJob = await _context.Jobs.FirstOrDefaultAsync(x => x.Id == job.Id);
                if (cancelledJob != null)
                {
                    cancelledJob.Status = JobStatus.Cancelled;
                    cancelledJob.ErrorCode = "Cancelled";
                    cancelledJob.ErrorMessage = ex.Message;
                    cancelledJob.FinishedAt = DateTimeOffset.UtcNow;
                    cancelledJob.LastHeartbeat = cancelledJob.FinishedAt;
                    cancelledJob.LeaseExpiresAt = null;
                    await _context.SaveChangesAsync();
                }

                await AppendJobLogAsync(job.Id, "Warning", $"Job cancelled: {ex.Message}");
                global::System.Console.WriteLine($"process-job cancelled id={job.Id}");
                return false;
            }

            await MarkJobTargetFailedAsync(job, ex.Message);
            job.Status = JobStatus.Failed;
            job.ErrorCode = "OperationCancelled";
            job.ErrorMessage = ex.Message;
            job.FinishedAt = DateTimeOffset.UtcNow;
            job.LastHeartbeat = job.FinishedAt;
            job.LeaseExpiresAt = null;
            await _context.SaveChangesAsync();

            await AppendJobLogAsync(job.Id, "Error", $"Job cancelled unexpectedly: {ex.Message}");
            global::System.Console.WriteLine($"process-job failed id={job.Id} error={ex.Message}");
            return false;
        }
        catch (Exception ex)
        {
            await MarkJobTargetFailedAsync(job, ex.Message);
            job.Status = JobStatus.Failed;
            job.ErrorCode = "ProcessJobError";
            job.ErrorMessage = ex.Message;
            job.FinishedAt = DateTimeOffset.UtcNow;
            job.LastHeartbeat = job.FinishedAt;
            job.LeaseExpiresAt = null;
            await _context.SaveChangesAsync();

            await AppendJobLogAsync(job.Id, "Error", $"Job failed: {ex.Message}");
            global::System.Console.WriteLine($"process-job failed id={job.Id} error={ex.Message}");
            return false;
        }
        finally
        {
            heartbeatCts.Cancel();
            try
            {
                await heartbeatTask;
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    private async Task<bool> IsJobCancelledAsync(Guid jobId)
    {
        return await _context.Jobs
            .AsNoTracking()
            .AnyAsync(x => x.Id == jobId && x.Status == JobStatus.Cancelled);
    }

    private async Task ThrowIfJobCancelledAsync(Guid? jobId)
    {
        if (!jobId.HasValue)
        {
            return;
        }

        var cancelled = await _context.Jobs
            .AsNoTracking()
            .AnyAsync(x => x.Id == jobId.Value && x.Status == JobStatus.Cancelled);
        if (cancelled)
        {
            throw new OperationCanceledException($"Job {jobId.Value} was cancelled.");
        }
    }

    private async Task RecoverExpiredConsoleJobLeasesAsync()
    {
        var now = DateTimeOffset.UtcNow;
        var recoverableJobs = await _context.Jobs
            .Where(x => x.Status == JobStatus.Running && x.LeaseExpiresAt != null && x.LeaseExpiresAt < now)
            .ToListAsync();

        if (recoverableJobs.Count == 0)
        {
            return;
        }

        foreach (var stuckJob in recoverableJobs)
        {
            stuckJob.RetryCount += 1;
            stuckJob.WorkerId = null;
            stuckJob.StartedAt = null;
            stuckJob.LastHeartbeat = now;
            stuckJob.LeaseExpiresAt = null;
            stuckJob.ErrorCode = "LeaseExpired";
            stuckJob.ErrorMessage = "Job lease expired before completion";
            stuckJob.Status = stuckJob.RetryCount < stuckJob.MaxRetries ? JobStatus.Pending : JobStatus.Failed;
        }

        await _context.SaveChangesAsync();
        global::System.Console.WriteLine($"recovered expired console jobs count={recoverableJobs.Count}");
    }

    public async Task PrintPlaylistListAsync(string[] commandArgs)
    {
        if (commandArgs.Length > 1)
        {
            global::System.Console.WriteLine("Usage: playlists [status]");
            return;
        }

        PlaylistStatus? statusFilter = null;
        if (commandArgs.Length == 1)
        {
            if (!Enum.TryParse<PlaylistStatus>(commandArgs[0], ignoreCase: true, out var parsedStatus))
            {
                global::System.Console.WriteLine("Invalid status. Example: playlists draft");
                return;
            }

            statusFilter = parsedStatus;
        }

        var playlistsQuery = _context.Playlists
            .AsNoTracking()
            .AsQueryable();

        if (statusFilter.HasValue)
        {
            playlistsQuery = playlistsQuery.Where(p => p.Status == statusFilter.Value);
        }

        var playlists = await playlistsQuery
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

        global::System.Console.WriteLine("playlist_id\tguid_id\ttitle\tis_folder_exists");
        foreach (var row in rows)
        {
            var title = string.IsNullOrWhiteSpace(row.Title) ? "-" : row.Title.Trim();
            global::System.Console.WriteLine($"{row.Id}\t{row.Id}\t{title}\t{row.IsFolderExists.ToString().ToLowerInvariant()}");
        }
    }

    private async Task<string> ProcessScheduledJobAsync(Job job)
    {
        if (string.IsNullOrWhiteSpace(job.PayloadJson))
        {
            throw new InvalidOperationException("Job payload_json is empty.");
        }

        var payload = JsonSerializer.Deserialize<ScheduledCommandPayload>(job.PayloadJson);
        if (payload == null)
        {
            throw new InvalidOperationException("Job payload_json could not be deserialized.");
        }

        var command = payload.Command?.Trim();
        if (string.IsNullOrWhiteSpace(command))
        {
            throw new InvalidOperationException("Job payload command is missing.");
        }

        return command.ToLowerInvariant() switch
        {
            "playlist-init" => await ProcessPlaylistInitJobAsync(job, payload),
            "generate-album-json" => await ProcessGenerateAlbumJsonJobAsync(job, payload),
            "run-prompt-generation" => await ProcessRunPromptGenerationJobAsync(job, payload),
            "generate-all-images" => await ProcessGenerateAllImagesJobAsync(job, payload),
            "generate-all-music" => await ProcessGenerateAllMusicJobAsync(job, payload),
            "track-create-youtube-video-thumbnail-v2" => await ProcessGenerateThumbnailsJobAsync(job, payload),
            "generate-media-local" => await ProcessGenerateVideosJobAsync(job, payload),
            "generate-youtube-playlist" => await ProcessGenerateYoutubePlaylistJobAsync(job, payload),
            "upload-youtube-videos" => await ProcessUploadYoutubeVideosJobAsync(job, payload),
            "add-youtube-videos-to-playlist" => await ProcessAddYoutubeVideosToPlaylistJobAsync(job, payload),
            "generate-youtube-engagements" => await ProcessGenerateYoutubeEngagementsJobAsync(job, payload),
            "post-youtube-engagement-comment" => await ProcessPostYoutubeEngagementCommentJobAsync(job, payload),
            "delete-album-release-temp-files" => await ProcessDeleteAlbumReleaseTempFilesJobAsync(job, payload),
            "generate-album-release-assets" => await ProcessGenerateAlbumReleaseAssetsJobAsync(job, payload),
            "upload-album-release-youtube" => await ProcessUploadAlbumReleaseToYoutubeJobAsync(job, payload),
            "create-track-loop" => await ProcessCreateTrackLoopJobAsync(job, payload),
            _ => throw new InvalidOperationException($"Unsupported scheduled command: {command}")
        };
    }

    private async Task<string> ProcessDeleteAlbumReleaseTempFilesJobAsync(Job job, ScheduledCommandPayload payload)
    {
        var arguments = payload.Arguments.Deserialize<CreateDeleteAlbumReleaseTempFilesJobArguments>();
        if (arguments == null)
        {
            throw new InvalidOperationException("delete-album-release-temp-files arguments are invalid.");
        }

        var release = await _context.AlbumReleases.FirstOrDefaultAsync(x => x.Id == arguments.AlbumReleaseId);
        if (release == null)
        {
            throw new InvalidOperationException($"Album release not found: {arguments.AlbumReleaseId}");
        }

        await AppendJobLogAsync(job.Id, "Info", $"Delete album release temp files started album_release_id={release.Id}");
        var deleted = false;
        if (!string.IsNullOrWhiteSpace(release.TempRootPath) && Directory.Exists(release.TempRootPath))
        {
            Directory.Delete(release.TempRootPath, recursive: true);
            deleted = true;
        }

        release.TempRootPath = null;
        release.OutputVideoPath = null;
        release.ThumbnailPath = null;
        release.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await _context.SaveChangesAsync();

        await AppendJobLogAsync(job.Id, "Info", $"Delete album release temp files completed album_release_id={release.Id} deleted={deleted}");

        return JsonSerializer.Serialize(new
        {
            ok = true,
            albumReleaseId = release.Id,
            deleted
        });
    }

    private async Task<string> ProcessGenerateAlbumReleaseAssetsJobAsync(Job job, ScheduledCommandPayload payload)
    {
        var arguments = payload.Arguments.Deserialize<CreateGenerateAlbumReleaseAssetsJobArguments>();
        if (arguments == null)
        {
            throw new InvalidOperationException("generate-album-release-assets arguments are invalid.");
        }

        var release = await _context.AlbumReleases.FirstOrDefaultAsync(x => x.Id == arguments.AlbumReleaseId);
        if (release == null)
        {
            throw new InvalidOperationException($"Album release not found: {arguments.AlbumReleaseId}");
        }

        var playlist = await _context.Playlists
            .Include(x => x.Tracks)
            .FirstOrDefaultAsync(x => x.Id == release.PlaylistId);
        if (playlist == null)
        {
            throw new InvalidOperationException($"Playlist not found: {release.PlaylistId}");
        }

        release.Status = AlbumReleaseStatus.Preparing;
        release.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await _context.SaveChangesAsync();

        await AppendJobLogAsync(job.Id, "Info", $"Generate album release assets started album_release_id={release.Id}");

        var result = await GenerateAlbumReleaseAssetsAsync(job.Id, release, playlist);

        release.OutputVideoPath = result.OutputVideoPath;
        release.ThumbnailPath = result.ThumbnailPath;
        release.TempRootPath = result.TempRootPath;
        release.Status = AlbumReleaseStatus.Prepared;
        release.UpdatedAtUtc = DateTimeOffset.UtcNow;
        release.FinishedAtUtc = null;
        await _context.SaveChangesAsync();

        await AppendJobLogAsync(job.Id, "Info", $"Generate album release assets completed album_release_id={release.Id} output={result.OutputVideoPath} thumbnail={result.ThumbnailPath}");

        return JsonSerializer.Serialize(new
        {
            ok = true,
            albumReleaseId = release.Id,
            result.OutputVideoPath,
            result.ThumbnailPath,
            result.TempRootPath
        });
    }

    private async Task<string> ProcessUploadAlbumReleaseToYoutubeJobAsync(Job job, ScheduledCommandPayload payload)
    {
        var arguments = payload.Arguments.Deserialize<CreateUploadAlbumReleaseToYoutubeJobArguments>();
        if (arguments == null)
        {
            throw new InvalidOperationException("upload-album-release-youtube arguments are invalid.");
        }

        var release = await _context.AlbumReleases.FirstOrDefaultAsync(x => x.Id == arguments.AlbumReleaseId);
        if (release == null)
        {
            throw new InvalidOperationException($"Album release not found: {arguments.AlbumReleaseId}");
        }

        var playlist = await _context.Playlists
            .Include(x => x.Tracks)
            .FirstOrDefaultAsync(x => x.Id == release.PlaylistId);
        if (playlist == null)
        {
            throw new InvalidOperationException($"Playlist not found: {release.PlaylistId}");
        }

        release.Status = AlbumReleaseStatus.Uploading;
        release.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await _context.SaveChangesAsync();

        await AppendJobLogAsync(job.Id, "Info", $"Upload album release to YouTube started album_release_id={release.Id}");

        var upload = await UploadAlbumReleaseToYoutubeAsync(job.Id, release, playlist);

        release.YoutubeVideoId = upload.VideoId;
        release.YoutubeUrl = upload.Url;
        release.Status = AlbumReleaseStatus.Uploaded;
        release.FinishedAtUtc = DateTimeOffset.UtcNow;
        release.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await _context.SaveChangesAsync();

        await AppendJobLogAsync(job.Id, "Info", $"Upload album release to YouTube completed album_release_id={release.Id} youtube_video_id={upload.VideoId}");

        return JsonSerializer.Serialize(new
        {
            ok = true,
            albumReleaseId = release.Id,
            upload.VideoId,
            upload.Url
        });
    }

    private async Task<string> ProcessGenerateAlbumJsonJobAsync(Job job, ScheduledCommandPayload payload)
        => await ProcessRunPromptGenerationInternalAsync(job, payload, "generate-album-json");

    private async Task<string> ProcessRunPromptGenerationJobAsync(Job job, ScheduledCommandPayload payload)
        => await ProcessRunPromptGenerationInternalAsync(job, payload, "run-prompt-generation");

    private async Task<string> ProcessRunPromptGenerationInternalAsync(Job job, ScheduledCommandPayload payload, string commandName)
    {
        var arguments = payload.Arguments.Deserialize<CreatePromptGenerationJobArguments>();
        if (arguments == null)
        {
            throw new InvalidOperationException($"{commandName} arguments are invalid.");
        }

        var generation = await _context.PromptGenerations
            .Include(x => x.Template)
            .Include(x => x.Outputs)
            .FirstOrDefaultAsync(x => x.Id == arguments.PromptGenerationId);
        if (generation == null)
        {
            throw new InvalidOperationException($"Prompt generation not found: {arguments.PromptGenerationId}");
        }

        if (generation.Template == null)
        {
            throw new InvalidOperationException($"Prompt template not found for generation: {arguments.PromptGenerationId}");
        }

        generation.Status = PromptGenerationStatus.Running;
        generation.StartedAtUtc = DateTimeOffset.UtcNow;
        generation.FinishedAtUtc = null;
        generation.ErrorMessage = null;
        await _context.SaveChangesAsync();

        await AppendJobLogAsync(job.Id, "Info", $"Prompt generation started prompt_generation_id={generation.Id} provider={generation.Provider} model={generation.Model ?? generation.Template.DefaultModel ?? "gemini-3.1-pro"}");

        var model = string.IsNullOrWhiteSpace(generation.Model)
            ? (generation.Template.DefaultModel ?? "gemini-3.1-pro")
            : generation.Model.Trim();

        var executionStopwatch = Stopwatch.StartNew();
        var execution = await ExecutePromptGenerationAsync(
            generation.Provider,
            model,
            generation.ResolvedSystemPrompt,
            generation.ResolvedUserPrompt);
        executionStopwatch.Stop();

        if (!execution.Success)
        {
            generation.Status = PromptGenerationStatus.Failed;
            generation.FinishedAtUtc = DateTimeOffset.UtcNow;
            generation.ErrorMessage = execution.ErrorMessage;
            generation.LatencyMs = (int)executionStopwatch.ElapsedMilliseconds;
            await _context.SaveChangesAsync();

            await AppendJobLogAsync(job.Id, "Error", $"Prompt generation failed prompt_generation_id={generation.Id}", execution.ErrorMessage);
            throw new InvalidOperationException(execution.ErrorMessage ?? "Prompt generation failed.");
        }

        var normalizedRawText = NormalizeReturnedJson(execution.RawText);
        var isValidJson = TryFormatJson(normalizedRawText, out var formattedJson, out var validationErrors);
        var expectsJson = string.Equals(generation.Template.OutputMode, "json", StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrWhiteSpace(generation.Template.SchemaKey);
        var runSucceeded = expectsJson ? isValidJson : true;

        var output = new PromptGenerationOutput
        {
            Id = Guid.NewGuid(),
            PromptGenerationId = generation.Id,
            OutputType = string.IsNullOrWhiteSpace(generation.Template.OutputMode) ? "text" : generation.Template.OutputMode.Trim(),
            OutputLabel = "Primary Output",
            OutputText = execution.RawText,
            OutputJson = isValidJson ? formattedJson : null,
            IsPrimary = true,
            IsValid = runSucceeded,
            ValidationErrors = runSucceeded ? null : validationErrors,
            ProviderResponseJson = execution.RawResponseJson,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        generation.Outputs.Add(output);
        generation.Status = runSucceeded ? PromptGenerationStatus.Completed : PromptGenerationStatus.Failed;
        generation.FinishedAtUtc = DateTimeOffset.UtcNow;
        generation.ErrorMessage = runSucceeded ? null : validationErrors;
        generation.LatencyMs = (int)executionStopwatch.ElapsedMilliseconds;
        generation.RunMetadataJson = JsonSerializer.Serialize(new
        {
            provider = generation.Provider,
            model = execution.Model,
            finishReason = execution.FinishReason
        });
        generation.TokenUsageJson = execution.UsageJson ?? "{}";
        await _context.SaveChangesAsync();

        await AppendJobLogAsync(
            job.Id,
            runSucceeded ? "Info" : "Warning",
            $"Prompt generation completed prompt_generation_id={generation.Id} valid_json={isValidJson.ToString().ToLowerInvariant()}",
            JsonSerializer.Serialize(new
            {
                generationId = generation.Id,
                outputId = output.Id,
                model,
                outputType = output.OutputType,
                validationErrors
            }));

        if (!runSucceeded)
        {
            throw new InvalidOperationException(validationErrors ?? "Prompt generation returned invalid output.");
        }

        return JsonSerializer.Serialize(new
        {
            ok = true,
            command = commandName,
            promptGenerationId = generation.Id,
            outputId = output.Id,
            model,
            outputType = output.OutputType,
            validationErrors = (string?)null
        });
    }

    private async Task<string> ProcessPlaylistInitJobAsync(Job job, ScheduledCommandPayload payload)
    {
        var arguments = payload.Arguments.Deserialize<CreatePlaylistInitJobArguments>();
        if (arguments == null)
        {
            throw new InvalidOperationException("playlist-init arguments are invalid.");
        }

        var playlist = await _context.Playlists
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == arguments.PlaylistId);
        if (playlist == null)
        {
            throw new InvalidOperationException($"Playlist not found: {arguments.PlaylistId}");
        }

        await AppendJobLogAsync(job.Id, "Info", $"Playlist init started playlist_id={arguments.PlaylistId}");
        await RunPlaylistPipelineAsync(arguments.PlaylistId);
        await AppendJobLogAsync(job.Id, "Info", $"Playlist init completed playlist_id={arguments.PlaylistId}");

        return JsonSerializer.Serialize(new
        {
            ok = true,
            command = payload.Command,
            playlistId = arguments.PlaylistId
        });
    }

    private async Task<string> ProcessGenerateAllImagesJobAsync(Job job, ScheduledCommandPayload payload)
    {
        var arguments = payload.Arguments.Deserialize<CreateGenerateAllImagesJobArguments>();
        if (arguments == null)
        {
            throw new InvalidOperationException("generate-all-images arguments are invalid.");
        }

        var playlist = await _context.Playlists
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == arguments.PlaylistId);
        if (playlist == null)
        {
            throw new InvalidOperationException($"Playlist not found: {arguments.PlaylistId}");
        }

        await AppendJobLogAsync(job.Id, "Info", $"Generate all images started playlist_id={arguments.PlaylistId}");
        var succeeded = await RunGenerateAllImagesAsync(new[] { arguments.PlaylistId.ToString() }, job.Id);
        if (!succeeded)
        {
            throw new InvalidOperationException($"Generate all images failed playlist_id={arguments.PlaylistId}");
        }

        await AppendJobLogAsync(job.Id, "Info", $"Generate all images completed playlist_id={arguments.PlaylistId}");

        return JsonSerializer.Serialize(new
        {
            ok = true,
            command = payload.Command,
            playlistId = arguments.PlaylistId
        });
    }

    private async Task<string> ProcessGenerateAllMusicJobAsync(Job job, ScheduledCommandPayload payload)
    {
        var arguments = payload.Arguments.Deserialize<CreateGenerateAllMusicJobArguments>();
        if (arguments == null)
        {
            throw new InvalidOperationException("generate-all-music arguments are invalid.");
        }

        var playlist = await _context.Playlists
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == arguments.PlaylistId);
        if (playlist == null)
        {
            throw new InvalidOperationException($"Playlist not found: {arguments.PlaylistId}");
        }

        await AppendJobLogAsync(job.Id, "Info", $"Generate all music started playlist_id={arguments.PlaylistId}");
        await RunGenerateAllMusicAsync(new[] { arguments.PlaylistId.ToString() });
        await AppendJobLogAsync(job.Id, "Info", $"Generate all music completed playlist_id={arguments.PlaylistId}");

        return JsonSerializer.Serialize(new
        {
            ok = true,
            command = payload.Command,
            playlistId = arguments.PlaylistId
        });
    }

    private async Task<string> ProcessGenerateThumbnailsJobAsync(Job job, ScheduledCommandPayload payload)
    {
        var arguments = payload.Arguments.Deserialize<CreateGenerateThumbnailsJobArguments>();
        if (arguments == null)
        {
            throw new InvalidOperationException("track-create-youtube-video-thumbnail-v2 arguments are invalid.");
        }

        var playlist = await _context.Playlists
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == arguments.PlaylistId);
        if (playlist == null)
        {
            throw new InvalidOperationException($"Playlist not found: {arguments.PlaylistId}");
        }

        await AppendJobLogAsync(job.Id, "Info", $"Generate thumbnails started playlist_id={arguments.PlaylistId}");
        await RunTrackCreateYoutubeVideoThumbnailV2Async(new[] { arguments.PlaylistId.ToString() });
        await AppendJobLogAsync(job.Id, "Info", $"Generate thumbnails completed playlist_id={arguments.PlaylistId}");

        return JsonSerializer.Serialize(new
        {
            ok = true,
            command = payload.Command,
            playlistId = arguments.PlaylistId
        });
    }

    private async Task<string> ProcessGenerateVideosJobAsync(Job job, ScheduledCommandPayload payload)
    {
        var arguments = payload.Arguments.Deserialize<CreateGenerateVideosJobArguments>();
        if (arguments == null)
        {
            throw new InvalidOperationException("generate-media-local arguments are invalid.");
        }

        var playlist = await _context.Playlists
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == arguments.PlaylistId);
        if (playlist == null)
        {
            throw new InvalidOperationException($"Playlist not found: {arguments.PlaylistId}");
        }

        var profile = arguments.Profile?.Trim().ToLowerInvariant();
        if (profile is not ("legacy" or "quality" or "fast"))
        {
            throw new InvalidOperationException($"Unsupported generate-media-local profile: {arguments.Profile}");
        }

        await AppendJobLogAsync(job.Id, "Info", $"Generate videos started playlist_id={arguments.PlaylistId} profile={profile}");
        await RunGenerateMediaLocalAsync(new[] { arguments.PlaylistId.ToString(), profile });
        await AppendJobLogAsync(job.Id, "Info", $"Generate videos completed playlist_id={arguments.PlaylistId} profile={profile}");

        return JsonSerializer.Serialize(new
        {
            ok = true,
            command = payload.Command,
            playlistId = arguments.PlaylistId,
            profile
        });
    }

    private async Task<string> ProcessGenerateYoutubePlaylistJobAsync(Job job, ScheduledCommandPayload payload)
    {
        var arguments = payload.Arguments.Deserialize<CreateGenerateYoutubePlaylistJobArguments>();
        if (arguments == null)
        {
            throw new InvalidOperationException("generate-youtube-playlist arguments are invalid.");
        }

        var playlist = await _context.Playlists
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == arguments.PlaylistId);
        if (playlist == null)
        {
            throw new InvalidOperationException($"Playlist not found: {arguments.PlaylistId}");
        }

        var privacy = arguments.Privacy?.Trim().ToLowerInvariant();
        if (privacy is not ("private" or "unlisted" or "public"))
        {
            throw new InvalidOperationException($"Unsupported generate-youtube-playlist privacy: {arguments.Privacy}");
        }

        await AppendJobLogAsync(job.Id, "Info", $"Generate YouTube playlist started playlist_id={arguments.PlaylistId} privacy={privacy}");
        await RunGenerateYoutubePlaylistAsync(new[] { arguments.PlaylistId.ToString(), privacy });
        await AppendJobLogAsync(job.Id, "Info", $"Generate YouTube playlist completed playlist_id={arguments.PlaylistId} privacy={privacy}");

        return JsonSerializer.Serialize(new
        {
            ok = true,
            command = payload.Command,
            playlistId = arguments.PlaylistId,
            privacy
        });
    }

    private async Task<string> ProcessUploadYoutubeVideosJobAsync(Job job, ScheduledCommandPayload payload)
    {
        var arguments = payload.Arguments.Deserialize<CreateUploadYoutubeVideosJobArguments>();
        if (arguments == null)
        {
            throw new InvalidOperationException("upload-youtube-videos arguments are invalid.");
        }

        var playlist = await _context.Playlists
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == arguments.PlaylistId);
        if (playlist == null)
        {
            throw new InvalidOperationException($"Playlist not found: {arguments.PlaylistId}");
        }

        await AppendJobLogAsync(job.Id, "Info", $"Upload YouTube videos started playlist_id={arguments.PlaylistId}");
        await RunUploadYoutubeVideosAsync(new[] { arguments.PlaylistId.ToString() });
        await AppendJobLogAsync(job.Id, "Info", $"Upload YouTube videos completed playlist_id={arguments.PlaylistId}");

        return JsonSerializer.Serialize(new
        {
            ok = true,
            command = payload.Command,
            playlistId = arguments.PlaylistId
        });
    }

    private async Task<string> ProcessAddYoutubeVideosToPlaylistJobAsync(Job job, ScheduledCommandPayload payload)
    {
        var arguments = payload.Arguments.Deserialize<CreateAddYoutubeVideosToPlaylistJobArguments>();
        if (arguments == null)
        {
            throw new InvalidOperationException("add-youtube-videos-to-playlist arguments are invalid.");
        }

        var playlist = await _context.Playlists
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == arguments.PlaylistId);
        if (playlist == null)
        {
            throw new InvalidOperationException($"Playlist not found: {arguments.PlaylistId}");
        }

        await AppendJobLogAsync(job.Id, "Info", $"Add YouTube videos to playlist started playlist_id={arguments.PlaylistId}");
        await RunAddYoutubeVideosToPlaylistAsync(new[] { arguments.PlaylistId.ToString() });
        await AppendJobLogAsync(job.Id, "Info", $"Add YouTube videos to playlist completed playlist_id={arguments.PlaylistId}");

        return JsonSerializer.Serialize(new
        {
            ok = true,
            command = payload.Command,
            playlistId = arguments.PlaylistId
        });
    }

    private async Task<string> ProcessGenerateYoutubeEngagementsJobAsync(Job job, ScheduledCommandPayload payload)
    {
        var arguments = payload.Arguments.Deserialize<CreateGenerateYoutubeEngagementsJobArguments>();
        if (arguments == null)
        {
            throw new InvalidOperationException("generate-youtube-engagements arguments are invalid.");
        }

        var playlist = await _context.Playlists
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == arguments.PlaylistId);
        if (playlist == null)
        {
            throw new InvalidOperationException($"Playlist not found: {arguments.PlaylistId}");
        }

        await AppendJobLogAsync(job.Id, "Info", $"Generate YouTube engagements started playlist_id={arguments.PlaylistId}");
        var generatedCount = await GenerateYoutubeEngagementsForPlaylistAsync(job.Id, arguments.PlaylistId);
        await AppendJobLogAsync(job.Id, "Info", $"Generate YouTube engagements completed playlist_id={arguments.PlaylistId} count={generatedCount}");

        return JsonSerializer.Serialize(new
        {
            ok = true,
            command = payload.Command,
            playlistId = arguments.PlaylistId,
            generatedCount
        });
    }

    private async Task<string> ProcessPostYoutubeEngagementCommentJobAsync(Job job, ScheduledCommandPayload payload)
    {
        var arguments = payload.Arguments.Deserialize<CreatePostYoutubeEngagementCommentJobArguments>();
        if (arguments == null)
        {
            throw new InvalidOperationException("post-youtube-engagement-comment arguments are invalid.");
        }

        var engagement = await _context.YoutubeVideoEngagements.FirstOrDefaultAsync(x => x.Id == arguments.YoutubeVideoEngagementId);
        if (engagement == null)
        {
            throw new InvalidOperationException($"YouTube engagement not found: {arguments.YoutubeVideoEngagementId}");
        }

        if (string.IsNullOrWhiteSpace(engagement.YoutubeVideoId))
        {
            throw new InvalidOperationException("YouTube video id is missing.");
        }

        var message = engagement.FinalText?.Trim();
        if (string.IsNullOrWhiteSpace(message))
        {
            throw new InvalidOperationException("Final engagement message is empty.");
        }

        var mcpWorkingDirectory = Environment.GetEnvironmentVariable("YT_PRODUCER_MCP_YOUTUBE_WORKING_DIRECTORY");
        if (string.IsNullOrWhiteSpace(mcpWorkingDirectory))
        {
            throw new InvalidOperationException("YT_PRODUCER_MCP_YOUTUBE_WORKING_DIRECTORY is not configured.");
        }

        var mcpProject = Environment.GetEnvironmentVariable("YT_PRODUCER_MCP_YOUTUBE_PROJECT")
            ?? "OnlineTeamTools.MCP.YouTube/OnlineTeamTools.MCP.YouTube.csproj";

        await AppendJobLogAsync(job.Id, "Info", $"Post YouTube engagement started engagement_id={engagement.Id} video_id={engagement.YoutubeVideoId}");

        var postResult = await ExecuteYoutubeAddCommentAsync(
            mcpWorkingDirectory,
            mcpProject,
            engagement.YoutubeVideoId,
            message);

        if (!postResult.Success || string.IsNullOrWhiteSpace(postResult.CommentId))
        {
            engagement.Status = YoutubeVideoEngagementStatus.Failed;
            engagement.ErrorMessage = postResult.ErrorMessage ?? "Unknown YouTube comment posting error.";
            engagement.UpdatedAtUtc = DateTimeOffset.UtcNow;
            await _context.SaveChangesAsync();
            await AppendJobLogAsync(job.Id, "Error", $"Post YouTube engagement failed engagement_id={engagement.Id}", engagement.ErrorMessage);

            return JsonSerializer.Serialize(new
            {
                ok = false,
                command = payload.Command,
                engagementId = engagement.Id,
                error = engagement.ErrorMessage
            });
        }

        engagement.YoutubeCommentId = postResult.CommentId;
        engagement.PostedAtUtc = DateTimeOffset.UtcNow;
        engagement.Status = YoutubeVideoEngagementStatus.Posted;
        engagement.ErrorMessage = null;
        engagement.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await _context.SaveChangesAsync();

        await AppendJobLogAsync(job.Id, "Info", $"Post YouTube engagement completed engagement_id={engagement.Id} comment_id={postResult.CommentId}");

        return JsonSerializer.Serialize(new
        {
            ok = true,
            command = payload.Command,
            engagementId = engagement.Id,
            commentId = postResult.CommentId
        });
    }

    private async Task<string> ProcessCreateTrackLoopJobAsync(Job job, ScheduledCommandPayload payload)
    {
        var arguments = payload.Arguments.Deserialize<CreateTrackLoopJobArguments>();
        if (arguments == null)
        {
            throw new InvalidOperationException("create-track-loop arguments are invalid.");
        }

        var loop = await _context.TrackLoops.FirstOrDefaultAsync(x => x.Id == arguments.LoopId);
        if (loop == null)
        {
            throw new InvalidOperationException($"Track loop not found: {arguments.LoopId}");
        }

        var playlist = await _context.Playlists.FirstOrDefaultAsync(x => x.Id == arguments.PlaylistId);
        if (playlist == null)
        {
            throw new InvalidOperationException($"Playlist not found: {arguments.PlaylistId}");
        }

        var track = await _context.Tracks.FirstOrDefaultAsync(x => x.Id == arguments.TrackId && x.PlaylistId == arguments.PlaylistId);
        if (track == null)
        {
            throw new InvalidOperationException($"Track not found: {arguments.TrackId}");
        }

        var workingDirectory = Environment.GetEnvironmentVariable("YT_PRODUCER_PLAYLIST_WORKING_DIRECTORY");
        if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            throw new InvalidOperationException("YT_PRODUCER_PLAYLIST_WORKING_DIRECTORY is not configured.");
        }

        var playlistRoot = Path.Combine(workingDirectory, arguments.PlaylistId.ToString());
        if (!Directory.Exists(playlistRoot))
        {
            throw new InvalidOperationException($"Playlist folder not found: {playlistRoot}");
        }

        var loopWorkingDirectory = Environment.GetEnvironmentVariable("YT_PRODUCER_LOOP_WORKING_DIRECTORY");
        if (string.IsNullOrWhiteSpace(loopWorkingDirectory))
        {
            throw new InvalidOperationException("YT_PRODUCER_LOOP_WORKING_DIRECTORY is not configured.");
        }

        loop.Status = TrackLoopStatus.InProgress;
        loop.StartedAtUtc = DateTimeOffset.UtcNow;
        loop.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await _context.SaveChangesAsync();

        await AppendJobLogAsync(job.Id, "Info", $"Preparing loop work package loop_id={loop.Id} track_position={loop.TrackPosition} loops={loop.LoopCount}");

        Directory.CreateDirectory(loopWorkingDirectory);
        var loopsRoot = Path.Combine(loopWorkingDirectory, arguments.PlaylistId.ToString());
        var loopRoot = Path.Combine(loopsRoot, loop.Id.ToString());
        Directory.CreateDirectory(loopRoot);

        loop.SourceAudioPath = ResolvePreferredMediaFile(playlistRoot, loop.TrackPosition, AudioExtensions);
        loop.SourceImagePath = ResolvePreferredMediaFile(playlistRoot, loop.TrackPosition, ImageExtensions);
        loop.SourceVideoPath = ResolvePreferredMediaFile(playlistRoot, loop.TrackPosition, [".mp4", ".mov", ".webm"]);
        loop.ThumbnailPath = ResolvePreferredMediaFile(playlistRoot, loop.TrackPosition, ImageExtensions, "_thumbnail");
        loop.Title ??= track.Title;
        loop.Description ??= track.YouTubeTitle;

        if (string.IsNullOrWhiteSpace(loop.SourceVideoPath) || !File.Exists(loop.SourceVideoPath))
        {
            throw new InvalidOperationException($"Source video not found for track position {loop.TrackPosition}.");
        }

        var manifestPath = Path.Combine(loopRoot, "request.json");
        var concatListPath = Path.Combine(loopRoot, "segments.txt");
        var outputVideoPath = Path.Combine(loopRoot, $"{loop.TrackPosition}_loop_x{loop.LoopCount}.mp4");
        var metadata = new
        {
            jobId = job.Id,
            loopId = loop.Id,
            playlistId = loop.PlaylistId,
            trackId = loop.TrackId,
            trackPosition = loop.TrackPosition,
            loopCount = loop.LoopCount,
            sourceAudioPath = loop.SourceAudioPath,
            sourceImagePath = loop.SourceImagePath,
            sourceVideoPath = loop.SourceVideoPath,
            thumbnailPath = loop.ThumbnailPath,
            outputVideoPath,
            createdAtUtc = DateTimeOffset.UtcNow
        };
        var metadataJson = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(manifestPath, metadataJson);

        var concatLines = Enumerable.Range(0, loop.LoopCount)
            .Select(_ => $"file '{EscapeConcatFilePath(loop.SourceVideoPath)}'")
            .ToArray();
        await File.WriteAllLinesAsync(concatListPath, concatLines);

        await AppendJobLogAsync(job.Id, "Info", $"Rendering loop video output={outputVideoPath}");
        var ffmpegPath = Environment.GetEnvironmentVariable("FFMPEG_PATH") ?? "ffmpeg";
        var concatResult = await ExecuteLoopConcatAsync(ffmpegPath, concatListPath, outputVideoPath);
        if (!concatResult.Success)
        {
            throw new InvalidOperationException(concatResult.ErrorMessage);
        }

        var loopThumbnailPath = await CreateLoopThumbnailAsync(job.Id, loop, track, playlist, loopRoot);

        var loopYoutubeMetadata = await BuildLoopYoutubeMetadataAsync(track, playlist, loop, outputVideoPath);
        await AppendJobLogAsync(
            job.Id,
            "Info",
            $"Loop YouTube metadata prepared title={loopYoutubeMetadata.Title} duration_minutes={loopYoutubeMetadata.DurationMinutes}");

        var youtubeUpload = await UploadLoopToYoutubeAsync(
            job.Id,
            loop,
            outputVideoPath,
            loopThumbnailPath,
            loopYoutubeMetadata);
        if (!youtubeUpload.Success)
        {
            throw new InvalidOperationException(youtubeUpload.ErrorMessage ?? "Loop YouTube upload failed.");
        }

        loop.OutputVideoPath = outputVideoPath;
        loop.ThumbnailPath = loopThumbnailPath;
        loop.YoutubeVideoId = youtubeUpload.VideoId;
        loop.YoutubeUrl = youtubeUpload.Url;
        loop.Title = loopYoutubeMetadata.Title;
        loop.Description = loopYoutubeMetadata.Description;
        loop.FinishedAtUtc = DateTimeOffset.UtcNow;
        loop.UpdatedAtUtc = loop.FinishedAtUtc.Value;
        loop.Status = TrackLoopStatus.UploadedToYoutube;
        loop.Metadata = JsonSerializer.Serialize(new
        {
            requestManifestPath = manifestPath,
            concatListPath,
            ffmpegCommand = concatResult.CommandLine,
            jobId = job.Id,
            preparedAtUtc = DateTimeOffset.UtcNow,
            completedAtUtc = loop.FinishedAtUtc,
            loopThumbnailPath,
            youtubeVideoId = youtubeUpload.VideoId,
            youtubeUrl = youtubeUpload.Url,
            tags = loopYoutubeMetadata.Tags,
            hashtags = loopYoutubeMetadata.Hashtags,
            chapters = loopYoutubeMetadata.Chapters,
            categoryId = loopYoutubeMetadata.CategoryId,
            defaultLanguage = loopYoutubeMetadata.DefaultLanguage,
            defaultAudioLanguage = loopYoutubeMetadata.DefaultAudioLanguage,
            madeForKids = loopYoutubeMetadata.MadeForKids,
            loopRoot
        });
        await _context.SaveChangesAsync();

        await AppendJobLogAsync(job.Id, "Info", $"Loop video created output={outputVideoPath} thumbnail={loopThumbnailPath} youtube_video_id={youtubeUpload.VideoId}");

        var keepLoopTemp = string.Equals(
            Environment.GetEnvironmentVariable("YT_PRODUCER_LOOP_KEEP_TEMP"),
            "true",
            StringComparison.OrdinalIgnoreCase);

        if (!keepLoopTemp)
        {
            try
            {
                if (Directory.Exists(loopRoot))
                {
                    Directory.Delete(loopRoot, recursive: true);
                    await AppendJobLogAsync(job.Id, "Info", $"Loop working directory deleted path={loopRoot}");
                }
            }
            catch (Exception ex)
            {
                await AppendJobLogAsync(job.Id, "Warning", $"Loop working directory cleanup failed path={loopRoot} error={ex.Message}");
            }
        }
        else
        {
            await AppendJobLogAsync(job.Id, "Info", $"Loop working directory kept path={loopRoot}");
        }

        job.Progress = 100;
        job.LastHeartbeat = DateTimeOffset.UtcNow;

        return JsonSerializer.Serialize(new
        {
            ok = true,
            command = payload.Command,
            loopId = loop.Id,
            manifestPath,
            concatListPath,
            loopRoot,
            outputVideoPath,
            loopThumbnailPath,
            youtubeVideoId = youtubeUpload.VideoId,
            youtubeUrl = youtubeUpload.Url,
            sourceAudioPath = loop.SourceAudioPath,
            sourceImagePath = loop.SourceImagePath,
            sourceVideoPath = loop.SourceVideoPath
        });
    }

    private async Task AppendJobLogAsync(Guid jobId, string level, string message, string? metadata = null)
    {
        _context.JobLogs.Add(new JobLog
        {
            Id = Guid.NewGuid(),
            JobId = jobId,
            Level = level,
            Message = message,
            Metadata = metadata,
            CreatedAtUtc = DateTimeOffset.UtcNow
        });

        await _context.SaveChangesAsync();
    }

    private async Task MarkJobTargetFailedAsync(Job job, string errorMessage)
    {
        if (string.Equals(job.TargetType, "prompt_generation", StringComparison.OrdinalIgnoreCase) && job.TargetId is Guid promptGenerationId)
        {
            var promptGeneration = await _context.PromptGenerations.FirstOrDefaultAsync(x => x.Id == promptGenerationId);
            if (promptGeneration != null)
            {
                promptGeneration.Status = PromptGenerationStatus.Failed;
                promptGeneration.FinishedAtUtc = DateTimeOffset.UtcNow;
                promptGeneration.ErrorMessage = errorMessage;
                await _context.SaveChangesAsync();
            }

            return;
        }

        if (string.Equals(job.TargetType, "album_release", StringComparison.OrdinalIgnoreCase) && job.TargetId is Guid albumReleaseId)
        {
            var release = await _context.AlbumReleases.FirstOrDefaultAsync(x => x.Id == albumReleaseId);
            if (release != null)
            {
                release.Status = AlbumReleaseStatus.Failed;
                release.FinishedAtUtc = DateTimeOffset.UtcNow;
                release.UpdatedAtUtc = release.FinishedAtUtc.Value;
                release.Metadata = JsonSerializer.Serialize(new
                {
                    lastError = errorMessage,
                    failedAtUtc = release.FinishedAtUtc
                });
                await _context.SaveChangesAsync();
            }

            return;
        }

        if (!string.Equals(job.TargetType, "track_loop", StringComparison.OrdinalIgnoreCase) || job.TargetId is not Guid loopId)
        {
            return;
        }

        var loop = await _context.TrackLoops.FirstOrDefaultAsync(x => x.Id == loopId);
        if (loop == null)
        {
            return;
        }

        loop.Status = TrackLoopStatus.Failed;
        loop.FinishedAtUtc = DateTimeOffset.UtcNow;
        loop.UpdatedAtUtc = loop.FinishedAtUtc.Value;
        loop.Metadata = JsonSerializer.Serialize(new
        {
            lastError = errorMessage,
            failedAtUtc = loop.FinishedAtUtc
        });

        await _context.SaveChangesAsync();
    }

    private static string? ResolvePreferredMediaFile(
        string playlistRoot,
        int playlistPosition,
        IReadOnlyCollection<string> extensions,
        string? requiredNameSuffix = null)
    {
        if (string.IsNullOrWhiteSpace(playlistRoot) || !Directory.Exists(playlistRoot))
        {
            return null;
        }

        var normalizedExtensions = new HashSet<string>(extensions, StringComparer.OrdinalIgnoreCase);

        var candidates = Directory.EnumerateFiles(playlistRoot)
            .Where(path => normalizedExtensions.Contains(Path.GetExtension(path)))
            .Select(path => new
            {
                Path = path,
                VariantOrder = GetMediaVariantOrder(Path.GetFileNameWithoutExtension(path), playlistPosition, requiredNameSuffix)
            })
            .Where(x => x.VariantOrder.HasValue)
            .OrderBy(x => x.VariantOrder!.Value)
            .ThenBy(x => x.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return candidates.Count == 0 ? null : candidates[0].Path;
    }

    private static int? GetMediaVariantOrder(string fileNameWithoutExtension, int playlistPosition, string? requiredNameSuffix)
    {
        var positionPrefix = playlistPosition.ToString();
        var expectedBase = string.IsNullOrWhiteSpace(requiredNameSuffix)
            ? positionPrefix
            : $"{positionPrefix}{requiredNameSuffix}";

        if (fileNameWithoutExtension.Equals(expectedBase, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        var escapedSuffix = string.IsNullOrWhiteSpace(requiredNameSuffix)
            ? string.Empty
            : Regex.Escape(requiredNameSuffix);
        var match = Regex.Match(
            fileNameWithoutExtension,
            $"^{Regex.Escape(positionPrefix)}{escapedSuffix}_(\\d+)$",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        if (!match.Success)
        {
            return null;
        }

        return int.TryParse(match.Groups[1].Value, out var parsed) ? parsed : null;
    }

    private static string EscapeConcatFilePath(string filePath)
    {
        return filePath.Replace("\\", "\\\\").Replace("'", "'\\''");
    }

    private static async Task<LoopConcatResult> ExecuteLoopConcatAsync(string ffmpegPath, string concatListPath, string outputVideoPath)
    {
        var args = new[]
        {
            "-y",
            "-f", "concat",
            "-safe", "0",
            "-i", concatListPath,
            "-c", "copy",
            outputVideoPath
        };

        var startInfo = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = new Process { StartInfo = startInfo };
        process.Start();
        var stdOut = await process.StandardOutput.ReadToEndAsync();
        var stdErr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        var commandLine = $"{ffmpegPath} {string.Join(" ", args.Select(QuoteCommandArgument))}";
        if (process.ExitCode != 0)
        {
            var error = string.IsNullOrWhiteSpace(stdErr) ? stdOut.Trim() : stdErr.Trim();
            return new LoopConcatResult(false, commandLine, string.IsNullOrWhiteSpace(error) ? $"ffmpeg exited with code {process.ExitCode}" : error);
        }

        return new LoopConcatResult(true, commandLine, null);
    }

    private async Task<string?> CreateLoopThumbnailAsync(Guid jobId, TrackLoop loop, Track track, Playlist playlist, string loopRoot)
    {
        var imagePath = loop.SourceImagePath;
        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
        {
            await AppendJobLogAsync(jobId, "Warning", "Loop thumbnail skipped because source image is missing.");
            return null;
        }

        var extension = Path.GetExtension(imagePath);
        if (string.IsNullOrWhiteSpace(extension))
        {
            extension = ".jpg";
        }

        var outputPath = Path.Combine(loopRoot, $"{loop.TrackPosition}_loop_x{loop.LoopCount}_thumbnail{extension.ToLowerInvariant()}");
        if (File.Exists(outputPath))
        {
            File.Delete(outputPath);
        }

        var visualStyleHint = TryGetMetadataString(track.Metadata, "visualStyleHint");
        var headlineFont = ResolveThumbnailHeadlineFont(Environment.GetEnvironmentVariable("YT_PRODUCER_THUMBNAIL_HEADLINE_FONT"), visualStyleHint);
        var subheadlineFont = ResolveThumbnailSubheadlineFont(Environment.GetEnvironmentVariable("YT_PRODUCER_THUMBNAIL_SUBHEADLINE_FONT"), visualStyleHint);

        var request = new CreateYoutubeThumbnailRequest
        {
            ImagePath = imagePath,
            LogoPath = Environment.GetEnvironmentVariable("YT_PRODUCER_THUMBNAIL_LOGO_PATH"),
            Headline = "LOOP",
            Subheadline = ResolveThumbnailSubheadline(
                track,
                playlist,
                Environment.GetEnvironmentVariable("YT_PRODUCER_THUMBNAIL_SUBHEADLINE")),
            OutputPath = outputPath,
            Style = new CreateYoutubeThumbnailStyleRequest
            {
                HeadlineFont = headlineFont,
                SubheadlineFont = subheadlineFont,
                HeadlineColor = Environment.GetEnvironmentVariable("YT_PRODUCER_THUMBNAIL_HEADLINE_COLOR"),
                SubheadlineColor = Environment.GetEnvironmentVariable("YT_PRODUCER_THUMBNAIL_SUBHEADLINE_COLOR"),
                Shadow = true,
                Stroke = true
            }
        };

        var service = new YoutubeThumbnailService();
        var response = await service.CreateAsync(request, CancellationToken.None);
        if (!response.Ok)
        {
            throw new InvalidOperationException("Loop thumbnail generation returned ok=false.");
        }

        return response.OutputPath;
    }

    private async Task<YoutubeUploadMetadata> BuildLoopYoutubeMetadataAsync(Track track, Playlist playlist, TrackLoop loop, string outputVideoPath)
    {
        var durationSeconds = await ProbeMediaDurationSecondsAsync(outputVideoPath)
            ?? await ProbeMediaDurationSecondsAsync(loop.SourceVideoPath)
            ?? ParseTrackDurationSeconds(track.Duration)
            ?? 0d;
        return _youtubeSeoService.BuildLoopUploadMetadata(
            track,
            playlist,
            durationSeconds,
            playlist.YoutubePlaylistId);
    }

    private async Task<AlbumReleaseAssetsResult> GenerateAlbumReleaseAssetsAsync(Guid jobId, AlbumRelease release, Playlist playlist)
    {
        var workingDirectory = Environment.GetEnvironmentVariable("YT_PRODUCER_PLAYLIST_WORKING_DIRECTORY");
        if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            throw new InvalidOperationException("YT_PRODUCER_PLAYLIST_WORKING_DIRECTORY is not configured.");
        }

        var playlistFolderPath = Path.Combine(workingDirectory, GetPlaylistFolderName(playlist.Id));
        if (!Directory.Exists(playlistFolderPath))
        {
            throw new InvalidOperationException($"Playlist folder not found: {playlistFolderPath}");
        }

        var tempRoot = string.IsNullOrWhiteSpace(release.TempRootPath)
            ? Path.Combine(workingDirectory, "tmp", "album-releases", playlist.Id.ToString())
            : release.TempRootPath;
        Directory.CreateDirectory(tempRoot);

        var orderedTracks = playlist.Tracks
            .OrderBy(x => x.PlaylistPosition)
            .ToList();
        if (orderedTracks.Count == 0)
        {
            throw new InvalidOperationException("Album release cannot be generated because the playlist has no tracks.");
        }

        var concatListPath = Path.Combine(tempRoot, "segments.txt");
        var outputVideoPath = Path.Combine(tempRoot, "album_release.mp4");
        var manifestPath = Path.Combine(tempRoot, "manifest.json");

        var concatLines = new List<string>(orderedTracks.Count);
        var trackManifest = new List<object>(orderedTracks.Count);
        double offsetSeconds = 0d;

        foreach (var track in orderedTracks)
        {
            var videoPath = ResolveVideoPathForPosition(playlistFolderPath, track.PlaylistPosition);
            if (string.IsNullOrWhiteSpace(videoPath) || !File.Exists(videoPath))
            {
                throw new InvalidOperationException($"Missing rendered video for track {track.PlaylistPosition}: {track.Title}");
            }

            concatLines.Add($"file '{EscapeConcatFilePath(videoPath)}'");

            var durationSeconds = await ProbeMediaDurationSecondsAsync(videoPath)
                ?? ParseTrackDurationSeconds(track.Duration)
                ?? 0d;
            trackManifest.Add(new
            {
                track.PlaylistPosition,
                track.Title,
                StartOffset = FormatYoutubeTimestamp(offsetSeconds),
                DurationSeconds = durationSeconds,
                VideoPath = videoPath
            });
            offsetSeconds += durationSeconds;
        }

        await File.WriteAllLinesAsync(concatListPath, concatLines);

        var ffmpegPath = Environment.GetEnvironmentVariable("FFMPEG_PATH") ?? "ffmpeg";
        var concatResult = await ExecuteLoopConcatAsync(ffmpegPath, concatListPath, outputVideoPath);
        if (!concatResult.Success)
        {
            throw new InvalidOperationException(concatResult.ErrorMessage ?? "Album release concat failed.");
        }

        var thumbnailPath = await CreateAlbumReleaseThumbnailAsync(jobId, release, playlist, playlistFolderPath, tempRoot, offsetSeconds);

        var metadata = new
        {
            release.Id,
            release.PlaylistId,
            release.Title,
            release.Description,
            totalDurationSeconds = offsetSeconds,
            createdAtUtc = DateTimeOffset.UtcNow,
            concatListPath,
            ffmpegCommand = concatResult.CommandLine,
            tracks = trackManifest
        };
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }));

        return new AlbumReleaseAssetsResult(outputVideoPath, thumbnailPath, tempRoot);
    }

    private async Task<string?> CreateAlbumReleaseThumbnailAsync(
        Guid jobId,
        AlbumRelease release,
        Playlist playlist,
        string playlistFolderPath,
        string tempRoot,
        double totalDurationSeconds)
    {
        var outputPath = Path.Combine(tempRoot, "album_release_thumbnail.jpg");
        var dedicatedThumbnailPath = ResolveAlbumReleaseSourceThumbnailPath(playlistFolderPath);
        if (!string.IsNullOrWhiteSpace(dedicatedThumbnailPath) && File.Exists(dedicatedThumbnailPath))
        {
            await SaveAlbumReleaseThumbnailAsJpegAsync(dedicatedThumbnailPath, outputPath);
            return outputPath;
        }

        var imagePaths = playlist.Tracks
            .OrderBy(x => x.PlaylistPosition)
            .Select(track => ResolveImagePathForPosition(playlistFolderPath, track.PlaylistPosition))
            .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (imagePaths.Count == 0)
        {
            await AppendJobLogAsync(jobId, "Warning", "Album release thumbnail skipped because source images are missing.");
            return null;
        }

        var thumbnailVersion = ParseAlbumReleaseThumbnailVersion(release.Metadata);
        var tileCount = Math.Min(4, imagePaths.Count);
        var selected = Enumerable.Range(0, tileCount)
            .Select(index => imagePaths[(thumbnailVersion + index) % imagePaths.Count])
            .ToList();

        using var surface = SKSurface.Create(new SKImageInfo(1280, 720))
            ?? throw new InvalidOperationException("Failed to create album release thumbnail surface.");
        var canvas = surface.Canvas;
        canvas.Clear(new SKColor(8, 11, 14));

        var gap = 8;
        var tiles = new[]
        {
            new SKRect(0, 0, 640, 360),
            new SKRect(640, 0, 1280, 360),
            new SKRect(0, 360, 640, 720),
            new SKRect(640, 360, 1280, 720)
        };

        for (var index = 0; index < selected.Count; index++)
        {
            using var bitmap = SKBitmap.Decode(selected[index]);
            if (bitmap == null)
            {
                continue;
            }

            var target = tiles[index];
            target.Inflate(-gap, -gap);
            DrawAlbumReleaseTile(canvas, bitmap, target);
        }

        DrawAlbumReleaseOverlay(canvas);

        var title = BuildAlbumReleaseDisplayTitle(release.Title, playlist.Title);
        var subtitle = $"FULL ALBUM • {playlist.Tracks.Count} TRACKS • {FormatYoutubeTimestamp(totalDurationSeconds)}";
        DrawAlbumReleaseHeadline(canvas, title, new SKRect(54, 500, 1226, 638));
        DrawAlbumReleaseSubtitle(canvas, subtitle, new SKPoint(58, 666));

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? tempRoot);
        using var image = surface.Snapshot();
        using var encoded = image.Encode(SKEncodedImageFormat.Jpeg, 92);
        using var fileStream = File.Open(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
        encoded.SaveTo(fileStream);
        return outputPath;
    }

    private static async Task SaveAlbumReleaseThumbnailAsJpegAsync(string sourcePath, string outputPath)
    {
        using var inputStream = File.OpenRead(sourcePath);
        using var managedStream = new SKManagedStream(inputStream);
        using var codec = SKCodec.Create(managedStream)
            ?? throw new InvalidOperationException($"Failed to decode album release thumbnail source: {sourcePath}");
        using var bitmap = SKBitmap.Decode(codec)
            ?? throw new InvalidOperationException($"Failed to render album release thumbnail source: {sourcePath}");

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Jpeg, 92);
        using var fileStream = File.Open(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
        encoded.SaveTo(fileStream);
        await fileStream.FlushAsync();
    }

    private static void DrawAlbumReleaseTile(SKCanvas canvas, SKBitmap bitmap, SKRect target)
    {
        var sourceAspect = bitmap.Width / (float)bitmap.Height;
        var targetAspect = target.Width / target.Height;

        SKRect sourceRect;
        if (sourceAspect > targetAspect)
        {
            var cropWidth = bitmap.Height * targetAspect;
            var left = (bitmap.Width - cropWidth) / 2f;
            sourceRect = new SKRect(left, 0, left + cropWidth, bitmap.Height);
        }
        else
        {
            var cropHeight = bitmap.Width / targetAspect;
            var top = (bitmap.Height - cropHeight) / 2f;
            sourceRect = new SKRect(0, top, bitmap.Width, top + cropHeight);
        }

        using var borderPaint = new SKPaint
        {
            IsAntialias = true,
            Color = new SKColor(255, 255, 255, 28),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1
        };

        canvas.DrawBitmap(bitmap, sourceRect, target);
        canvas.DrawRoundRect(target, 24, 24, borderPaint);
    }

    private static void DrawAlbumReleaseOverlay(SKCanvas canvas)
    {
        using var vertical = new SKPaint
        {
            IsAntialias = true,
            Shader = SKShader.CreateLinearGradient(
                new SKPoint(0, 720),
                new SKPoint(0, 250),
                [new SKColor(3, 8, 12, 248), new SKColor(3, 8, 12, 24)],
                [0f, 1f],
                SKShaderTileMode.Clamp)
        };
        canvas.DrawRect(new SKRect(0, 220, 1280, 720), vertical);

        using var leftGlow = new SKPaint
        {
            IsAntialias = true,
            Shader = SKShader.CreateRadialGradient(
                new SKPoint(210, 590),
                620,
                [new SKColor(0, 0, 0, 172), new SKColor(0, 0, 0, 0)],
                [0f, 1f],
                SKShaderTileMode.Clamp)
        };
        canvas.DrawRect(new SKRect(0, 0, 1280, 720), leftGlow);
    }

    private static void DrawAlbumReleaseHeadline(SKCanvas canvas, string text, SKRect bounds)
    {
        var lines = BuildAlbumReleaseHeadlineLines(text);
        if (lines.Count == 0)
        {
            return;
        }

        var typeface = ResolveAlbumReleaseHeadlineTypeface();
        using var fillPaint = new SKPaint
        {
            IsAntialias = true,
            Color = new SKColor(245, 239, 22),
            Typeface = typeface,
            Style = SKPaintStyle.Fill
        };
        using var strokePaint = new SKPaint
        {
            IsAntialias = true,
            Color = new SKColor(6, 8, 10, 245),
            Typeface = typeface,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 10,
            StrokeJoin = SKStrokeJoin.Round
        };
        using var shadowPaint = new SKPaint
        {
            IsAntialias = true,
            Color = new SKColor(0, 0, 0, 185),
            Typeface = typeface,
            Style = SKPaintStyle.Fill,
            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 4)
        };

        var textSize = 108f;
        while (textSize > 44f)
        {
            fillPaint.TextSize = textSize;
            strokePaint.TextSize = textSize;
            shadowPaint.TextSize = textSize;

            var widest = lines.Max(line => fillPaint.MeasureText(line));
            var totalHeight = fillPaint.FontSpacing * 0.84f * lines.Count;
            if (widest <= bounds.Width && totalHeight <= bounds.Height)
            {
                break;
            }

            textSize -= 2f;
        }

        var lineHeight = fillPaint.FontSpacing * 0.84f;
        var y = bounds.Top - fillPaint.FontMetrics.Ascent;
        foreach (var line in lines)
        {
            using var textPath = fillPaint.GetTextPath(line, bounds.Left, y);
            canvas.DrawPath(textPath, shadowPaint);
            canvas.DrawPath(textPath, strokePaint);
            canvas.DrawPath(textPath, fillPaint);
            DrawAlbumReleaseDistress(canvas, textPath, line);
            y += lineHeight;
        }
    }

    private static void DrawAlbumReleaseSubtitle(SKCanvas canvas, string text, SKPoint origin)
    {
        var sanitized = SanitizeAlbumReleaseText(text).ToUpperInvariant();
        using var textPaint = new SKPaint
        {
            IsAntialias = true,
            Color = new SKColor(255, 255, 255, 230),
            Typeface = SKTypeface.FromFamilyName("Arial", SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright),
            TextSize = 28
        };
        using var shadowPaint = new SKPaint
        {
            IsAntialias = true,
            Color = new SKColor(0, 0, 0, 160),
            Typeface = textPaint.Typeface,
            TextSize = textPaint.TextSize,
            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 3)
        };

        var y = origin.Y - textPaint.FontMetrics.Ascent;
        canvas.DrawText(sanitized, origin.X + 3, y + 4, shadowPaint);
        canvas.DrawText(sanitized, origin.X, y, textPaint);
    }

    private static List<string> BuildAlbumReleaseHeadlineLines(string text)
    {
        var sanitized = SanitizeAlbumReleaseText(text).ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(sanitized))
        {
            return new List<string>();
        }

        var parts = sanitized.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Trim())
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .ToList();

        if (parts.Count >= 2)
        {
            var secondary = string.Join(" ", parts.Skip(1))
                .Replace("FULL ALBUM", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Trim();

            return new List<string> { parts[0], secondary }
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Take(2)
                .ToList();
        }

        var words = sanitized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length <= 4)
        {
            return new List<string> { sanitized };
        }

        var midpoint = (int)Math.Ceiling(words.Length / 2d);
        return new List<string>
        {
            string.Join(" ", words.Take(midpoint)),
            string.Join(" ", words.Skip(midpoint))
        };
    }

    private static string BuildAlbumReleaseDisplayTitle(string? releaseTitle, string playlistTitle)
    {
        var raw = string.IsNullOrWhiteSpace(releaseTitle) ? $"{playlistTitle} | Full Album" : releaseTitle.Trim();
        var sanitized = SanitizeAlbumReleaseText(raw);
        sanitized = Regex.Replace(sanitized, "\\bFULL ALBUM\\b", string.Empty, RegexOptions.IgnoreCase).Trim(' ', '|', '-', '•');
        return string.IsNullOrWhiteSpace(sanitized) ? "WORKOUT ALBUM" : sanitized;
    }

    private static string SanitizeAlbumReleaseText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var normalized = text.Normalize(NormalizationForm.FormKC);
        var builder = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            if (char.IsLetterOrDigit(ch) || char.IsWhiteSpace(ch))
            {
                builder.Append(ch);
                continue;
            }

            if ("|&:+-/()'.,•".Contains(ch))
            {
                builder.Append(ch);
                continue;
            }

            builder.Append(' ');
        }

        return Regex.Replace(builder.ToString(), "\\s+", " ").Trim();
    }

    private static SKTypeface ResolveAlbumReleaseHeadlineTypeface()
    {
        return SKTypeface.FromFamilyName("Impact", SKFontStyleWeight.Black, SKFontStyleWidth.Condensed, SKFontStyleSlant.Upright)
            ?? SKTypeface.FromFamilyName("Arial Narrow", SKFontStyleWeight.Bold, SKFontStyleWidth.Condensed, SKFontStyleSlant.Upright)
            ?? SKTypeface.FromFamilyName("Arial", SKFontStyleWeight.Black, SKFontStyleWidth.Condensed, SKFontStyleSlant.Upright);
    }

    private static void DrawAlbumReleaseDistress(SKCanvas canvas, SKPath textPath, string seedText)
    {
        var bounds = textPath.Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        var seed = StringComparer.Ordinal.GetHashCode(seedText);
        var random = new Random(seed);

        canvas.Save();
        canvas.ClipPath(textPath, SKClipOperation.Intersect, true);

        using var erasePaint = new SKPaint
        {
            IsAntialias = true,
            Color = new SKColor(6, 8, 10, 255),
            BlendMode = SKBlendMode.SrcOver
        };

        var dotCount = Math.Max(10, (int)(bounds.Width / 55f));
        for (var i = 0; i < dotCount; i++)
        {
            var x = bounds.Left + (float)random.NextDouble() * bounds.Width;
            var y = bounds.Top + (float)random.NextDouble() * bounds.Height;
            var radius = 1.5f + (float)random.NextDouble() * 4.5f;
            canvas.DrawCircle(x, y, radius, erasePaint);
        }

        var scratchCount = Math.Max(4, (int)(bounds.Width / 220f));
        erasePaint.StrokeWidth = 2.5f;
        erasePaint.Style = SKPaintStyle.Stroke;
        erasePaint.StrokeCap = SKStrokeCap.Round;
        for (var i = 0; i < scratchCount; i++)
        {
            var x = bounds.Left + (float)random.NextDouble() * bounds.Width;
            var y = bounds.Top + (float)random.NextDouble() * bounds.Height;
            var height = 8f + (float)random.NextDouble() * 18f;
            canvas.DrawLine(x, y, x, y + height, erasePaint);
        }

        canvas.Restore();
    }

    private async Task<YoutubeUploadVideoResult> UploadAlbumReleaseToYoutubeAsync(Guid jobId, AlbumRelease release, Playlist playlist)
    {
        if (string.IsNullOrWhiteSpace(release.OutputVideoPath) || !File.Exists(release.OutputVideoPath))
        {
            throw new InvalidOperationException("Album release output video is missing. Generate assets first.");
        }

        var mcpWorkingDirectory = Environment.GetEnvironmentVariable("YT_PRODUCER_MCP_YOUTUBE_WORKING_DIRECTORY");
        if (string.IsNullOrWhiteSpace(mcpWorkingDirectory))
        {
            throw new InvalidOperationException("YT_PRODUCER_MCP_YOUTUBE_WORKING_DIRECTORY is not configured.");
        }

        var mcpProject = Environment.GetEnvironmentVariable("YT_PRODUCER_MCP_YOUTUBE_PROJECT")
            ?? "OnlineTeamTools.MCP.YouTube/OnlineTeamTools.MCP.YouTube.csproj";
        var allowedRoot = Environment.GetEnvironmentVariable("YT_PRODUCER_YOUTUBE_ALLOWED_ROOT")
            ?? Environment.GetEnvironmentVariable("YT_PRODUCER_PLAYLIST_WORKING_DIRECTORY");

        var durationSeconds = await ProbeMediaDurationSecondsAsync(release.OutputVideoPath)
            ?? playlist.Tracks.Sum(x => ParseTrackDurationSeconds(x.Duration) ?? 0d);
        var uploadMetadata = BuildAlbumReleaseUploadMetadata(release, playlist, durationSeconds);

        var publishState = await GetOrCreateYoutubeLastPublishedDateAsync();
        var publishAt = GetNextYoutubePublishSlotUtc(publishState.LastPublishedDate);
        var result = await ExecuteYoutubeUploadVideoAsync(
            mcpWorkingDirectory,
            mcpProject,
            allowedRoot,
            release.OutputVideoPath,
            uploadMetadata,
            "private",
            publishAt);

        if (!result.Success || string.IsNullOrWhiteSpace(result.VideoId))
        {
            throw new InvalidOperationException(result.ErrorMessage ?? "Album release upload failed.");
        }

        if (!string.IsNullOrWhiteSpace(release.ThumbnailPath) && File.Exists(release.ThumbnailPath))
        {
            await AppendJobLogAsync(jobId, "Info", $"Album release thumbnail upload started video_id={result.VideoId}");
            var thumbnailResult = await ExecuteYoutubeUploadThumbnailAsync(
                mcpWorkingDirectory,
                mcpProject,
                allowedRoot,
                result.VideoId,
                release.ThumbnailPath);

            if (!thumbnailResult.Success)
            {
                throw new InvalidOperationException($"album release video uploaded but thumbnail failed: {thumbnailResult.ErrorMessage}");
            }
        }

        if (publishAt > publishState.LastPublishedDate)
        {
            publishState.LastPublishedDate = publishAt;
            publishState.VideoId = result.VideoId;
            await _context.SaveChangesAsync();
        }

        return result;
    }

    private YoutubeUploadMetadata BuildAlbumReleaseUploadMetadata(AlbumRelease release, Playlist playlist, double durationSeconds)
    {
        var orderedTracks = playlist.Tracks
            .OrderBy(x => x.PlaylistPosition)
            .ToList();

        var title = string.IsNullOrWhiteSpace(release.Title)
            ? $"{playlist.Title} | Full Album"
            : release.Title.Trim();

        var description = string.IsNullOrWhiteSpace(release.Description)
            ? BuildAlbumReleaseDescription(playlist, orderedTracks)
            : release.Description.Trim();

        if (!string.IsNullOrWhiteSpace(playlist.YoutubePlaylistId))
        {
            description = $"{description}{Environment.NewLine}{Environment.NewLine}Listen to the playlist: https://www.youtube.com/playlist?list={playlist.YoutubePlaylistId}";
        }

        var brandHandle = Environment.GetEnvironmentVariable("YT_PRODUCER_YOUTUBE_CHANNEL_HANDLE");
        if (!string.IsNullOrWhiteSpace(brandHandle))
        {
            description = $"{description}{Environment.NewLine}Subscribe: https://www.youtube.com/{brandHandle.Trim().TrimStart('@')}";
        }

        var tags = BuildAlbumReleaseTags(playlist, orderedTracks);
        var hashtags = tags
            .Take(5)
            .Select(tag => $"#{NormalizeHashtag(tag)}")
            .Where(tag => tag.Length > 1)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (hashtags.Count > 0 && !description.Contains('#'))
        {
            description = $"{description}{Environment.NewLine}{Environment.NewLine}{string.Join(" ", hashtags)}";
        }

        var chapters = BuildAlbumReleaseChapters(orderedTracks);

        return new YoutubeUploadMetadata(
            title,
            description.Trim(),
            tags,
            hashtags,
            chapters,
            10,
            "en",
            "en",
            false,
            Math.Max(1, (int)Math.Ceiling(durationSeconds / 60d)));
    }

    private static string BuildAlbumReleaseDescription(Playlist playlist, IReadOnlyList<Track> tracks)
    {
        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(playlist.Description))
        {
            lines.Add(playlist.Description.Trim());
            lines.Add(string.Empty);
        }

        lines.Add("Tracklist:");
        double offsetSeconds = 0d;
        foreach (var track in tracks)
        {
            lines.Add($"{FormatYoutubeTimestamp(offsetSeconds)} - {track.Title}");
            offsetSeconds += ParseTrackDurationSeconds(track.Duration) ?? 0d;
        }

        return string.Join(Environment.NewLine, lines).Trim();
    }

    private static IReadOnlyList<string> BuildAlbumReleaseTags(Playlist playlist, IReadOnlyList<Track> tracks)
    {
        var values = new List<string>();

        if (!string.IsNullOrWhiteSpace(playlist.Theme))
        {
            values.Add(playlist.Theme);
        }

        if (!string.IsNullOrWhiteSpace(playlist.Title))
        {
            values.Add(playlist.Title);
            values.AddRange(playlist.Title.Split([' ', '⚡', '|', '-', ':'], StringSplitOptions.RemoveEmptyEntries));
        }

        foreach (var style in tracks.Select(x => x.Style).Where(x => !string.IsNullOrWhiteSpace(x)).Cast<string>())
        {
            values.Add(style);
        }

        return values
            .Select(value => value.Trim())
            .Where(value => value.Length >= 3)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .ToList();
    }

    private static IReadOnlyList<string> BuildAlbumReleaseChapters(IReadOnlyList<Track> tracks)
    {
        var chapters = new List<string>(tracks.Count);
        double offsetSeconds = 0d;
        foreach (var track in tracks)
        {
            chapters.Add($"{FormatYoutubeTimestamp(offsetSeconds)} - {track.Title}");
            offsetSeconds += ParseTrackDurationSeconds(track.Duration) ?? 0d;
        }

        return chapters;
    }

    private static int ParseAlbumReleaseThumbnailVersion(string? metadata)
    {
        if (string.IsNullOrWhiteSpace(metadata))
        {
            return 0;
        }

        try
        {
            using var document = JsonDocument.Parse(metadata);
            return document.RootElement.TryGetProperty("ThumbnailVersion", out var value) && value.TryGetInt32(out var parsed)
                ? Math.Max(0, parsed)
                : 0;
        }
        catch (JsonException)
        {
            return 0;
        }
    }

    private static string FormatYoutubeTimestamp(double totalSeconds)
    {
        var safe = Math.Max(0, (int)Math.Round(totalSeconds));
        var hours = safe / 3600;
        var minutes = (safe % 3600) / 60;
        var seconds = safe % 60;

        return hours > 0
            ? $"{hours}:{minutes:00}:{seconds:00}"
            : $"{minutes}:{seconds:00}";
    }

    private static string NormalizeHashtag(string value)
    {
        var compact = Regex.Replace(value, "[^a-zA-Z0-9]+", string.Empty);
        return compact.Length == 0 ? "Music" : compact;
    }

    private async Task<YoutubeUploadVideoResult> UploadLoopToYoutubeAsync(
        Guid jobId,
        TrackLoop loop,
        string videoPath,
        string? thumbnailPath,
        YoutubeUploadMetadata uploadMetadata)
    {
        var mcpWorkingDirectory = Environment.GetEnvironmentVariable("YT_PRODUCER_MCP_YOUTUBE_WORKING_DIRECTORY");
        if (string.IsNullOrWhiteSpace(mcpWorkingDirectory))
        {
            return YoutubeUploadVideoResult.Fail("YT_PRODUCER_MCP_YOUTUBE_WORKING_DIRECTORY is not configured.");
        }

        var mcpProject = Environment.GetEnvironmentVariable("YT_PRODUCER_MCP_YOUTUBE_PROJECT")
            ?? "OnlineTeamTools.MCP.YouTube/OnlineTeamTools.MCP.YouTube.csproj";

        var loopWorkingDirectory = Environment.GetEnvironmentVariable("YT_PRODUCER_LOOP_WORKING_DIRECTORY");
        var allowedRoot = Environment.GetEnvironmentVariable("YT_PRODUCER_YOUTUBE_ALLOWED_ROOT")
            ?? loopWorkingDirectory
            ?? Environment.GetEnvironmentVariable("YT_PRODUCER_PLAYLIST_WORKING_DIRECTORY");

        var publishState = await GetOrCreateYoutubeLastPublishedDateAsync();
        var publishAt = GetNextYoutubePublishSlotUtc(publishState.LastPublishedDate);
        var scheduledPrivacy = "private";

        await AppendJobLogAsync(
            jobId,
            "Info",
            $"Loop YouTube upload scheduled video_path={videoPath} publish_at_utc={publishAt:yyyy-MM-ddTHH:mm:ssZ} privacy={scheduledPrivacy}");

        await AppendJobLogAsync(
            jobId,
            "Info",
            $"Loop YouTube video upload started playlist_id={loop.PlaylistId} track_position={loop.TrackPosition}");

        var result = await ExecuteYoutubeUploadVideoAsync(
            mcpWorkingDirectory,
            mcpProject,
            allowedRoot,
            videoPath,
            uploadMetadata,
            scheduledPrivacy,
            publishAt);

        if (!result.Success || string.IsNullOrWhiteSpace(result.VideoId))
        {
            await AppendJobLogAsync(
                jobId,
                "Error",
                $"Loop YouTube video upload failed error={result.ErrorMessage}");
            return result;
        }

        await AppendJobLogAsync(
            jobId,
            "Info",
            $"Loop YouTube video upload completed video_id={result.VideoId} url={result.Url}");

        if (!string.IsNullOrWhiteSpace(thumbnailPath) && File.Exists(thumbnailPath))
        {
            await AppendJobLogAsync(
                jobId,
                "Info",
                $"Loop YouTube thumbnail upload started thumbnail_path={thumbnailPath} video_id={result.VideoId}");

            var thumbnailResult = await ExecuteYoutubeUploadThumbnailAsync(
                mcpWorkingDirectory,
                mcpProject,
                allowedRoot,
                result.VideoId,
                thumbnailPath);

            if (!thumbnailResult.Success)
            {
                await AppendJobLogAsync(
                    jobId,
                    "Error",
                    $"Loop YouTube thumbnail upload failed video_id={result.VideoId} error={thumbnailResult.ErrorMessage}");
                return YoutubeUploadVideoResult.Fail($"video uploaded but thumbnail failed: {thumbnailResult.ErrorMessage}");
            }

            await AppendJobLogAsync(
                jobId,
                "Info",
                $"Loop YouTube thumbnail upload completed video_id={result.VideoId}");
        }
        else
        {
            await AppendJobLogAsync(
                jobId,
                "Warning",
                $"Loop YouTube thumbnail upload skipped because thumbnail file is missing path={thumbnailPath ?? "-"}");
        }

        if (publishAt > publishState.LastPublishedDate)
        {
            publishState.LastPublishedDate = publishAt;
            publishState.VideoId = result.VideoId;
            await _context.SaveChangesAsync();

            await AppendJobLogAsync(
                jobId,
                "Info",
                $"Loop publish state updated next_last_published_date={publishAt:yyyy-MM-ddTHH:mm:ssZ} video_id={result.VideoId}");
        }
        else
        {
            await AppendJobLogAsync(
                jobId,
                "Info",
                $"Loop publish state unchanged existing_last_published_date={publishState.LastPublishedDate:yyyy-MM-ddTHH:mm:ssZ}");
        }

        return result;
    }

    private static async Task<double?> ProbeMediaDurationSecondsAsync(string? mediaPath)
    {
        if (string.IsNullOrWhiteSpace(mediaPath) || !File.Exists(mediaPath))
        {
            return null;
        }

        var ffprobePath = Environment.GetEnvironmentVariable("FFPROBE_PATH") ?? "ffprobe";
        var startInfo = new ProcessStartInfo
        {
            FileName = ffprobePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-v");
        startInfo.ArgumentList.Add("error");
        startInfo.ArgumentList.Add("-show_entries");
        startInfo.ArgumentList.Add("format=duration");
        startInfo.ArgumentList.Add("-of");
        startInfo.ArgumentList.Add("default=noprint_wrappers=1:nokey=1");
        startInfo.ArgumentList.Add(mediaPath);

        using var process = new Process { StartInfo = startInfo };
        process.Start();
        var stdOut = await process.StandardOutput.ReadToEndAsync();
        await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            return null;
        }

        return double.TryParse(stdOut.Trim(), out var parsed)
            ? parsed
            : null;
    }

    private static double? ParseTrackDurationSeconds(string? duration)
    {
        if (string.IsNullOrWhiteSpace(duration))
        {
            return null;
        }

        var parts = duration.Split(':', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 2 &&
            int.TryParse(parts[0], out var minutes) &&
            int.TryParse(parts[1], out var seconds))
        {
            return (minutes * 60) + seconds;
        }

        if (parts.Length == 3 &&
            int.TryParse(parts[0], out var hours) &&
            int.TryParse(parts[1], out minutes) &&
            int.TryParse(parts[2], out seconds))
        {
            return (hours * 3600) + (minutes * 60) + seconds;
        }

        if (TimeSpan.TryParse(duration, out var parsed))
        {
            return parsed.TotalSeconds;
        }

        return null;
    }

    private static string QuoteCommandArgument(string arg)
    {
        if (string.IsNullOrEmpty(arg))
        {
            return "\"\"";
        }

        return arg.Any(char.IsWhiteSpace) || arg.Contains('"')
            ? $"\"{arg.Replace("\"", "\\\"")}\""
            : arg;
    }

    public async Task RunGenerateMediaAsync(string[] commandArgs)
    {
        Guid? targetPlaylistId = null;
        if (commandArgs.Length > 0)
        {
            if (!Guid.TryParse(commandArgs[0], out var parsedPlaylistId))
            {
                global::System.Console.WriteLine("Usage: generate-media [playlistId]");
                return;
            }

            targetPlaylistId = parsedPlaylistId;
        }

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

        var playlistsQuery = _context.Playlists
            .AsNoTracking()
            .OrderByDescending(p => p.CreatedAtUtc)
            .Select(p => new { p.Id, p.Title })
            .AsQueryable();

        if (targetPlaylistId.HasValue)
        {
            playlistsQuery = playlistsQuery.Where(p => p.Id == targetPlaylistId.Value);
        }

        var playlists = await playlistsQuery.ToListAsync();

        if (playlists.Count == 0)
        {
            if (targetPlaylistId.HasValue)
            {
                global::System.Console.WriteLine($"Playlist not found: {targetPlaylistId.Value}");
            }

            global::System.Console.WriteLine(
                "generate-media summary playlists_scanned=0 playlists_locked=0 candidates=0 scheduled=0 failed=0 skipped_completed=0 skipped_in_progress=0");
            return;
        }

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

    public async Task RunGenerateMediaLocalAsync(string[] commandArgs)
    {
        if (commandArgs.Length < 1)
        {
            global::System.Console.WriteLine("Usage: generate-media-local <playlistId> [legacy|quality|fast]");
            return;
        }

        if (!Guid.TryParse(commandArgs[0], out var playlistId))
        {
            global::System.Console.WriteLine("Invalid playlistId.");
            return;
        }

        var profileName = commandArgs.Length > 1 ? commandArgs[1] : "quality";
        var profile = ResolveLocalMediaRenderProfile(profileName);
        if (profile is null)
        {
            global::System.Console.WriteLine("Invalid renderer profile. Supported values: legacy, quality, fast");
            return;
        }

        var workingDirectory = Environment.GetEnvironmentVariable("YT_PRODUCER_PLAYLIST_WORKING_DIRECTORY");
        if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            global::System.Console.WriteLine("YT_PRODUCER_PLAYLIST_WORKING_DIRECTORY is not configured.");
            return;
        }

        var mediaWorkingDirectory = Environment.GetEnvironmentVariable("YT_PRODUCER_MCP_MEDIA_WORKING_DIRECTORY");
        if (string.IsNullOrWhiteSpace(mediaWorkingDirectory))
        {
            global::System.Console.WriteLine("YT_PRODUCER_MCP_MEDIA_WORKING_DIRECTORY is not configured.");
            return;
        }

        var staleMinutes = ResolveStaleInProgressMinutes();
        var parallelism = ResolveMediaParallelism();
        var playlist = await _context.Playlists
            .AsNoTracking()
            .Include(p => p.Tracks)
            .FirstOrDefaultAsync(p => p.Id == playlistId);

        if (playlist == null)
        {
            global::System.Console.WriteLine($"Playlist not found: {playlistId}");
            return;
        }

        var playlistFolderPath = Path.Combine(workingDirectory, GetPlaylistFolderName(playlist.Id));
        if (!Directory.Exists(playlistFolderPath))
        {
            global::System.Console.WriteLine($"Playlist folder not found: {playlistFolderPath}");
            return;
        }

        CleanupStalePlaylistLock(playlistFolderPath, TimeSpan.FromMinutes(staleMinutes));
        using var playlistLock = TryAcquirePlaylistLock(playlistFolderPath);
        if (playlistLock == null)
        {
            global::System.Console.WriteLine($"skip locked playlist {playlist.Id} ({playlist.Title})");
            return;
        }

        await SetPlaylistStatusAsync(playlistId, PlaylistStatus.VideoInProgress);

        var ffmpegPath = Environment.GetEnvironmentVariable("FFMPEG_PATH") ?? "ffmpeg";
        var ffprobePath = Environment.GetEnvironmentVariable("FFPROBE_PATH") ?? "ffprobe";
        var runner = new FfmpegRunner();
        var workingDirectoryService = new WorkingDirectoryService(mediaWorkingDirectory, playlistFolderPath);
        var audioProbeService = new AudioProbeService(ffprobePath, runner);
        var audioAnalysisService = new AudioAnalysisService(ffmpegPath, runner);
        var frameRenderService = new FrameRenderService(ffmpegPath, ffprobePath, runner);
        var frameRenderServiceV6 = new FrameRenderServiceV6(ffmpegPath, ffprobePath, runner);
        var videoEncodeService = new VideoEncodeService(ffmpegPath, runner, playlistFolderPath);
        var state = LoadMediaGenerationState(playlistFolderPath);
        ExpireStaleInProgressEntries(state, TimeSpan.FromMinutes(staleMinutes));
        using var renderSemaphore = new SemaphoreSlim(parallelism, parallelism);
        var playlistStateLock = new SemaphoreSlim(1, 1);
        var summaryLock = new object();
        var trackLocks = new ConcurrentDictionary<int, SemaphoreSlim>();

        var tracksByPosition = playlist.Tracks
            .GroupBy(t => t.PlaylistPosition)
            .ToDictionary(g => g.Key, g => g.OrderBy(t => t.CreatedAtUtc).First());

        var candidates = DiscoverRenderCandidates(playlistFolderPath);
        var summary = new MediaGenerationSummary
        {
            PlaylistsScanned = 1,
            CandidatesFound = candidates.Count
        };

        try
        {
            var renderTasks = new List<Task>();
            foreach (var candidate in candidates)
            {
                renderTasks.Add(Task.Run(async () =>
                {
                    await renderSemaphore.WaitAsync();
                    try
                    {
                        var sortKey = ParseMediaSortKey(candidate.Key);
                        if (!tracksByPosition.TryGetValue(sortKey.Position, out var track))
                        {
                            lock (summaryLock)
                            {
                                summary.Failed++;
                            }

                            global::System.Console.WriteLine($"render failed playlist={playlist.Id} key={candidate.Key} error=track not found for position {sortKey.Position}");
                            return;
                        }

                        var trackLock = trackLocks.GetOrAdd(track.PlaylistPosition, _ => new SemaphoreSlim(1, 1));
                        await trackLock.WaitAsync();
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
                                    await UpdateTrackVideoGenerationAsync(
                                        track,
                                        candidate,
                                        profile,
                                        configure: item =>
                                        {
                                            item.Status = "completed";
                                            item.ProgressPercent = 100;
                                            item.OutputVideoPath = candidate.LocalVideoPath;
                                            item.ErrorMessage = null;
                                            item.FinishedAtUtc = DateTimeOffset.UtcNow;
                                            item.UpdatedAtUtc = DateTimeOffset.UtcNow;
                                        });
                                    lock (summaryLock)
                                    {
                                        summary.SkippedCompleted++;
                                    }
                                    return;
                                }

                                if (latestState.Entries.TryGetValue(candidate.Key, out var existing))
                                {
                                    if (string.Equals(existing.Status, "in_progress", StringComparison.OrdinalIgnoreCase))
                                    {
                                        lock (summaryLock)
                                        {
                                            summary.SkippedInProgress++;
                                        }
                                        return;
                                    }

                                    if (string.Equals(existing.Status, "completed", StringComparison.OrdinalIgnoreCase))
                                    {
                                        lock (summaryLock)
                                        {
                                            summary.SkippedCompleted++;
                                        }
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

                            global::System.Console.WriteLine($"render start playlist={playlist.Id} key={candidate.Key} renderer={profile.PublicName}");

                            var result = await RenderLocalMediaCandidateAsync(
                                playlist,
                                track,
                                candidate,
                                profile,
                                playlistFolderPath,
                                mediaWorkingDirectory,
                                workingDirectoryService,
                                audioProbeService,
                                audioAnalysisService,
                                frameRenderService,
                                frameRenderServiceV6,
                                videoEncodeService,
                                CancellationToken.None);

                            await playlistStateLock.WaitAsync();
                            try
                            {
                                var latestState = LoadMediaGenerationState(playlistFolderPath);
                                var entry = latestState.Entries.TryGetValue(candidate.Key, out var entryValue)
                                    ? entryValue
                                    : new MediaGenerationEntry();

                                if (result.Success)
                                {
                                    entry.Status = "completed";
                                    entry.LastError = null;
                                    entry.LastFinishedAtUtc = DateTimeOffset.UtcNow;
                                    entry.VideoPath = candidate.LocalVideoPath;
                                    lock (summaryLock)
                                    {
                                        summary.Scheduled++;
                                    }
                                }
                                else
                                {
                                    entry.Status = "failed";
                                    entry.LastError = result.ErrorMessage;
                                    entry.LastFinishedAtUtc = DateTimeOffset.UtcNow;
                                    lock (summaryLock)
                                    {
                                        summary.Failed++;
                                    }
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
                            trackLock.Release();
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
        finally
        {
            var finalStatus = summary.Failed > 0
                ? PlaylistStatus.Failed
                : PlaylistStatus.VideosGenerated;
            await SetPlaylistStatusAsync(playlistId, finalStatus);
        }

        global::System.Console.WriteLine(
            $"generate-media-local summary playlists_scanned={summary.PlaylistsScanned} playlists_locked={summary.PlaylistsLocked} candidates={summary.CandidatesFound} scheduled={summary.Scheduled} failed={summary.Failed} skipped_completed={summary.SkippedCompleted} skipped_in_progress={summary.SkippedInProgress} renderer={profile.PublicName}");
    }

    public async Task RunTestGenerateVideoAsync(string[] commandArgs)
    {
        if (commandArgs.Length < 1)
        {
            global::System.Console.WriteLine("Usage: test_generate_video <version>");
            global::System.Console.WriteLine("Example: test_generate_video v1");
            global::System.Console.WriteLine("Supported versions: v1, v2, v3, v4, v5, v6, v7");
            return;
        }

        var version = commandArgs[0].Trim().ToLowerInvariant();
        var profile = ResolveTestGenerateVideoProfile(version);
        if (profile is null)
        {
            global::System.Console.WriteLine("Unsupported version. Supported values: v1, v2, v3, v4, v5, v6, v7");
            return;
        }

        var imagePath = profile.ImagePath;
        var audioPath = profile.AudioPath;
        var tempDir = profile.TempDir;
        var outputDir = profile.OutputDir;
        var width = profile.Width;
        var height = profile.Height;
        var fps = profile.Fps;
        var eqBands = profile.EqBands;
        var videoBitrate = profile.VideoBitrate;
        var audioBitrate = profile.AudioBitrate;
        var seed = profile.Seed;
        var useGpu = profile.UseGpu;
        var keepTemp = profile.KeepTemp;
        var useRawPipe = profile.UseRawPipe;
        var rendererVariant = profile.RendererVariant;
        var outputFileNameOverride = profile.OutputFileNameOverride;

        if (!File.Exists(imagePath))
        {
            global::System.Console.WriteLine($"Image file not found: {imagePath}");
            return;
        }

        if (!File.Exists(audioPath))
        {
            global::System.Console.WriteLine($"Audio file not found: {audioPath}");
            return;
        }

        var ffmpegPath = Environment.GetEnvironmentVariable("FFMPEG_PATH") ?? "ffmpeg";
        var ffprobePath = Environment.GetEnvironmentVariable("FFPROBE_PATH") ?? "ffprobe";

        var runner = new FfmpegRunner();
        var workingDirectoryService = new WorkingDirectoryService(tempDir, outputDir);
        var audioProbeService = new AudioProbeService(ffprobePath, runner);
        var audioAnalysisService = new AudioAnalysisService(ffmpegPath, runner);
        var frameRenderService = new FrameRenderService(ffmpegPath, ffprobePath, runner);
        var frameRenderServiceV4 = new FrameRenderServiceV4(ffmpegPath, ffprobePath, runner);
        var frameRenderServiceV5 = new FrameRenderServiceV5(ffmpegPath, ffprobePath, runner);
        var frameRenderServiceV6 = new FrameRenderServiceV6(ffmpegPath, ffprobePath, runner);
        var videoEncodeService = new VideoEncodeService(ffmpegPath, runner, outputDir);

        WorkingDirectoryContext? job = null;
        StreamWriter? testLogWriter = null;
        string? testLogPath = null;
        try
        {
            job = workingDirectoryService.CreateJobDirectory(tempDir, outputDir);
            var analysisPath = Path.Combine(job.AnalysisDir, "analysis.json");
            testLogPath = Path.Combine(job.LogsDir, "test-generate-video.log");
            testLogWriter = new StreamWriter(testLogPath, append: false, Encoding.UTF8);

            global::System.Console.WriteLine($"test_generate_video start version={version}");
            global::System.Console.WriteLine($"seed={seed} fps={fps} eq_bands={eqBands} use_gpu={useGpu.ToString().ToLowerInvariant()} keep_temp={keepTemp.ToString().ToLowerInvariant()}");
            await testLogWriter.WriteLineAsync($"[{DateTimeOffset.UtcNow:O}] start version={version}");
            await testLogWriter.WriteLineAsync($"image={imagePath}");
            await testLogWriter.WriteLineAsync($"audio={audioPath}");
            await testLogWriter.WriteLineAsync($"temp_dir={tempDir}");
            await testLogWriter.WriteLineAsync($"output_dir={outputDir}");
            await testLogWriter.WriteLineAsync($"seed={seed} width={width} height={height} fps={fps} eq_bands={eqBands} video_bitrate={videoBitrate} audio_bitrate={audioBitrate} use_gpu={useGpu.ToString().ToLowerInvariant()} keep_temp={keepTemp.ToString().ToLowerInvariant()} use_raw_pipe={useRawPipe.ToString().ToLowerInvariant()} renderer={rendererVariant}");
            await testLogWriter.FlushAsync();

            var duration = await audioProbeService.ProbeDurationAsync(audioPath, CancellationToken.None);
            await testLogWriter.WriteLineAsync($"[{DateTimeOffset.UtcNow:O}] probe_duration_seconds={duration:F3}");
            await testLogWriter.FlushAsync();
            var analysis = await audioAnalysisService.AnalyzeAsync(
                audioPath,
                duration,
                fps,
                eqBands,
                analysisPath,
                CancellationToken.None);
            await testLogWriter.WriteLineAsync($"[{DateTimeOffset.UtcNow:O}] analysis_written={analysisPath}");
            await testLogWriter.WriteLineAsync($"analysis_frame_count={analysis.FrameCount}");
            await testLogWriter.FlushAsync();

            VideoEncodeResult encodeResult;
            if (useRawPipe)
            {
                await testLogWriter.WriteLineAsync($"[{DateTimeOffset.UtcNow:O}] raw_pipe_encode_start=true");
                await testLogWriter.FlushAsync();

                if (string.Equals(rendererVariant, "v4", StringComparison.OrdinalIgnoreCase))
                {
                    encodeResult = await EncodeRawFramesV4WithFallbackAsync(
                        frameRenderServiceV4,
                        ffmpegPath,
                        imagePath,
                        analysis,
                        audioPath,
                        width,
                        height,
                        fps,
                        seed,
                        videoBitrate,
                        audioBitrate,
                        outputDir,
                        outputFileNameOverride,
                        job.LogsDir,
                        useGpu,
                        CancellationToken.None);
                }
                else if (string.Equals(rendererVariant, "v5", StringComparison.OrdinalIgnoreCase))
                {
                    encodeResult = await EncodeRawFramesV5WithFallbackAsync(
                        frameRenderServiceV5,
                        ffmpegPath,
                        imagePath,
                        analysis,
                        audioPath,
                        width,
                        height,
                        fps,
                        seed,
                        videoBitrate,
                        audioBitrate,
                        outputDir,
                        outputFileNameOverride,
                        job.LogsDir,
                        useGpu,
                        CancellationToken.None);
                }
                else if (string.Equals(rendererVariant, "v6", StringComparison.OrdinalIgnoreCase))
                {
                    encodeResult = await EncodeRawFramesV6WithFallbackAsync(
                        frameRenderServiceV6,
                        ffmpegPath,
                        imagePath,
                        profile.LogoPath,
                        analysis,
                        audioPath,
                        width,
                        height,
                        fps,
                        seed,
                        videoBitrate,
                        audioBitrate,
                        outputDir,
                        outputFileNameOverride,
                        job.LogsDir,
                        useGpu,
                        CancellationToken.None);
                }
                else if (string.Equals(rendererVariant, "v7", StringComparison.OrdinalIgnoreCase))
                {
                    encodeResult = await EncodeRawFramesV7WithFallbackAsync(
                        frameRenderServiceV6,
                        ffmpegPath,
                        imagePath,
                        profile.LogoPath,
                        analysis,
                        audioPath,
                        width,
                        height,
                        fps,
                        seed,
                        videoBitrate,
                        audioBitrate,
                        outputDir,
                        outputFileNameOverride,
                        job.LogsDir,
                        useGpu,
                        CancellationToken.None);
                }
                else
                {
                    encodeResult = await EncodeRawFramesWithFallbackAsync(
                        frameRenderService,
                        ffmpegPath,
                        imagePath,
                        analysis,
                        audioPath,
                        width,
                        height,
                        fps,
                        seed,
                        videoBitrate,
                        audioBitrate,
                        outputDir,
                        job.LogsDir,
                        useGpu,
                        CancellationToken.None);
                }

                await testLogWriter.WriteLineAsync($"[{DateTimeOffset.UtcNow:O}] raw_pipe_encode_done={(encodeResult.Success ? "true" : "false")}");
                await testLogWriter.FlushAsync();
            }
            else
            {
                await frameRenderService.RenderFramesAsync(
                    imagePath,
                    analysis,
                    job.FramesDir,
                    width,
                    height,
                    seed,
                    cancellationToken: CancellationToken.None);
                await testLogWriter.WriteLineAsync($"[{DateTimeOffset.UtcNow:O}] frames_rendered_dir={job.FramesDir}");
                await testLogWriter.FlushAsync();

                encodeResult = await videoEncodeService.EncodeAsync(
                    job.FramesDir,
                    audioPath,
                    fps,
                    videoBitrate,
                    audioBitrate,
                    job.LogsDir,
                    outputDir,
                    useGpu,
                    CancellationToken.None);
            }

            if (!encodeResult.Success)
            {
                global::System.Console.WriteLine($"test_generate_video failed error={encodeResult.StderrTail}");
                await testLogWriter.WriteLineAsync($"[{DateTimeOffset.UtcNow:O}] encode_success=false");
                await testLogWriter.WriteLineAsync($"ffmpeg_command={encodeResult.CommandLine}");
                await testLogWriter.WriteLineAsync("stderr_tail_start");
                await testLogWriter.WriteLineAsync(encodeResult.StderrTail ?? string.Empty);
                await testLogWriter.WriteLineAsync("stderr_tail_end");
                await testLogWriter.FlushAsync();
                return;
            }

            global::System.Console.WriteLine($"test_generate_video completed output={encodeResult.OutputPath}");
            global::System.Console.WriteLine($"analysis={analysisPath}");
            global::System.Console.WriteLine($"ffmpeg_command={encodeResult.CommandLine}");
            await testLogWriter.WriteLineAsync($"[{DateTimeOffset.UtcNow:O}] encode_success=true");
            await testLogWriter.WriteLineAsync($"output={encodeResult.OutputPath}");
            await testLogWriter.WriteLineAsync($"analysis={analysisPath}");
            await testLogWriter.WriteLineAsync($"ffmpeg_command={encodeResult.CommandLine}");
            await testLogWriter.WriteLineAsync("stderr_tail_start");
            await testLogWriter.WriteLineAsync(encodeResult.StderrTail ?? string.Empty);
            await testLogWriter.WriteLineAsync("stderr_tail_end");
            await testLogWriter.FlushAsync();
            global::System.Console.WriteLine($"test_log={testLogPath}");
        }
        catch (Exception ex)
        {
            global::System.Console.WriteLine($"test_generate_video failed error={ex.Message}");
            if (testLogWriter is not null)
            {
                await testLogWriter.WriteLineAsync($"[{DateTimeOffset.UtcNow:O}] exception={ex}");
                await testLogWriter.FlushAsync();
            }
        }
        finally
        {
            if (testLogWriter is not null)
            {
                await testLogWriter.DisposeAsync();
            }

            if (!keepTemp && job is not null)
            {
                try
                {
                    if (!string.IsNullOrWhiteSpace(testLogPath) && File.Exists(testLogPath))
                    {
                        var preservedTestLogPath = Path.Combine(outputDir, "test-generate-video.last.log");
                        File.Copy(testLogPath, preservedTestLogPath, true);
                        global::System.Console.WriteLine($"preserved_test_log={preservedTestLogPath}");
                    }

                    var ffmpegLogPath = Path.Combine(job.LogsDir, "ffmpeg_stderr.txt");
                    if (File.Exists(ffmpegLogPath))
                    {
                        var preservedFfmpegLogPath = Path.Combine(outputDir, "ffmpeg_stderr.last.txt");
                        File.Copy(ffmpegLogPath, preservedFfmpegLogPath, true);
                        global::System.Console.WriteLine($"preserved_ffmpeg_log={preservedFfmpegLogPath}");
                    }
                    else
                    {
                        var rawFfmpegLogPath = Directory.EnumerateFiles(job.LogsDir, "ffmpeg_stderr_raw_*.txt")
                            .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase)
                            .FirstOrDefault();
                        if (!string.IsNullOrWhiteSpace(rawFfmpegLogPath) && File.Exists(rawFfmpegLogPath))
                        {
                            var preservedFfmpegLogPath = Path.Combine(outputDir, "ffmpeg_stderr.last.txt");
                            File.Copy(rawFfmpegLogPath, preservedFfmpegLogPath, true);
                            global::System.Console.WriteLine($"preserved_ffmpeg_log={preservedFfmpegLogPath}");
                        }
                    }
                }
                catch
                {
                    // Best-effort preservation before temp cleanup.
                }
            }

            if (!keepTemp && job is not null)
            {
                workingDirectoryService.TryCleanup(job);
            }
        }
    }

    private async Task<MediaExecutionResult> RenderLocalMediaCandidateAsync(
        Playlist playlist,
        Track track,
        RenderCandidate candidate,
        LocalMediaRenderProfile profile,
        string playlistFolderPath,
        string mediaWorkingDirectory,
        WorkingDirectoryService workingDirectoryService,
        AudioProbeService audioProbeService,
        AudioAnalysisService audioAnalysisService,
        FrameRenderService frameRenderService,
        FrameRenderServiceV6 frameRenderServiceV6,
        VideoEncodeService videoEncodeService,
        CancellationToken cancellationToken)
    {
        WorkingDirectoryContext? job = null;
        try
        {
            job = workingDirectoryService.CreateJobDirectory(mediaWorkingDirectory, playlistFolderPath);
            var analysisPath = Path.Combine(job.AnalysisDir, "analysis.json");
            var outputPath = candidate.LocalVideoPath ?? Path.Combine(playlistFolderPath, $"{candidate.Key}.mp4");

            await UpdateTrackVideoGenerationAsync(
                track,
                candidate,
                profile,
                configure: item =>
                {
                    item.Status = "in_progress";
                    item.ProgressPercent = 0;
                    item.ProgressCurrentFrame = 0;
                    item.ProgressTotalFrames = null;
                    item.TrackDurationSeconds = null;
                    item.ImagePath = candidate.ImagePath;
                    item.AudioPath = candidate.AudioPath;
                    item.TempDir = mediaWorkingDirectory;
                    item.OutputDir = playlistFolderPath;
                    item.Width = profile.Width;
                    item.Height = profile.Height;
                    item.Fps = profile.Fps;
                    item.EqBands = profile.EqBands;
                    item.VideoBitrate = profile.VideoBitrate;
                    item.AudioBitrate = profile.AudioBitrate;
                    item.Seed = profile.Seed;
                    item.UseGpu = profile.UseGpu;
                    item.KeepTemp = false;
                    item.UseRawPipe = profile.UseRawPipe;
                    item.RendererVariant = profile.RendererVariant;
                    item.OutputFileNameOverride = Path.GetFileName(outputPath);
                    item.LogoPath = profile.LogoPath;
                    item.OutputVideoPath = outputPath;
                    item.AnalysisPath = analysisPath;
                    item.ErrorMessage = null;
                    item.Metadata = JsonSerializer.Serialize(new
                    {
                        candidateKey = candidate.Key,
                        rendererProfile = profile.PublicName
                    });
                    item.StartedAtUtc = DateTimeOffset.UtcNow;
                    item.FinishedAtUtc = null;
                    item.UpdatedAtUtc = DateTimeOffset.UtcNow;
                });

            var duration = await audioProbeService.ProbeDurationAsync(candidate.AudioPath, cancellationToken);
            var analysis = await audioAnalysisService.AnalyzeAsync(
                candidate.AudioPath,
                duration,
                profile.Fps,
                profile.EqBands,
                analysisPath,
                cancellationToken);

            await UpdateTrackVideoGenerationAsync(
                track,
                candidate,
                profile,
                configure: item =>
                {
                    item.TrackDurationSeconds = duration;
                    item.ProgressCurrentFrame = 0;
                    item.ProgressTotalFrames = analysis.FrameCount;
                    item.AnalysisPath = analysisPath;
                    item.UpdatedAtUtc = DateTimeOffset.UtcNow;
                });

            var lastProgressUpdateUtc = DateTimeOffset.MinValue;
            Func<int, int, Task> onProgressAsync = async (current, total) =>
            {
                var now = DateTimeOffset.UtcNow;
                if (current < total && (now - lastProgressUpdateUtc).TotalMilliseconds < 300)
                {
                    return;
                }

                lastProgressUpdateUtc = now;
                var percent = total <= 0 ? 0 : (int)Math.Round(current * 100d / total);
                await UpdateTrackVideoGenerationAsync(
                    track,
                    candidate,
                    profile,
                    configure: item =>
                    {
                        item.Status = "in_progress";
                        item.ProgressCurrentFrame = current;
                        item.ProgressTotalFrames = total;
                        item.ProgressPercent = Math.Clamp(percent, 0, 99);
                        item.UpdatedAtUtc = DateTimeOffset.UtcNow;
                    });
            };

            VideoEncodeResult encodeResult;
            if (profile.UseRawPipe)
            {
                encodeResult = string.Equals(profile.RendererVariant, "v7", StringComparison.OrdinalIgnoreCase)
                    ? await EncodeRawFramesV7WithoutProgressFileAsync(
                        frameRenderServiceV6,
                        Environment.GetEnvironmentVariable("FFMPEG_PATH") ?? "ffmpeg",
                        candidate.ImagePath,
                        profile.LogoPath,
                        analysis,
                        candidate.AudioPath,
                        profile.Width,
                        profile.Height,
                        profile.Fps,
                        profile.Seed,
                        profile.VideoBitrate,
                        profile.AudioBitrate,
                        playlistFolderPath,
                        Path.GetFileName(outputPath),
                        job.LogsDir,
                        profile.UseGpu,
                        onProgressAsync,
                        cancellationToken)
                    : await EncodeRawFramesV6WithoutProgressFileAsync(
                        frameRenderServiceV6,
                        Environment.GetEnvironmentVariable("FFMPEG_PATH") ?? "ffmpeg",
                        candidate.ImagePath,
                        profile.LogoPath,
                        analysis,
                        candidate.AudioPath,
                        profile.Width,
                        profile.Height,
                        profile.Fps,
                        profile.Seed,
                        profile.VideoBitrate,
                        profile.AudioBitrate,
                        playlistFolderPath,
                        Path.GetFileName(outputPath),
                        job.LogsDir,
                        profile.UseGpu,
                        onProgressAsync,
                        cancellationToken);
            }
            else
            {
                await frameRenderService.RenderFramesAsync(
                    candidate.ImagePath,
                    analysis,
                    job.FramesDir,
                    profile.Width,
                    profile.Height,
                    profile.Seed,
                    cancellationToken,
                    onProgressAsync);

                encodeResult = await videoEncodeService.EncodeAsync(
                    job.FramesDir,
                    candidate.AudioPath,
                    profile.Fps,
                    profile.VideoBitrate,
                    profile.AudioBitrate,
                    job.LogsDir,
                    playlistFolderPath,
                    profile.UseGpu,
                    cancellationToken);
            }

            if (!encodeResult.Success)
            {
                await UpdateTrackVideoGenerationAsync(
                    track,
                    candidate,
                    profile,
                    configure: item =>
                    {
                        item.Status = "failed";
                        item.ErrorMessage = encodeResult.StderrTail;
                        item.FfmpegCommand = encodeResult.CommandLine;
                        item.FinishedAtUtc = DateTimeOffset.UtcNow;
                        item.UpdatedAtUtc = DateTimeOffset.UtcNow;
                    });
                return MediaExecutionResult.Fail(encodeResult.StderrTail);
            }

            if (!string.Equals(encodeResult.OutputPath, outputPath, StringComparison.OrdinalIgnoreCase)
                && File.Exists(encodeResult.OutputPath))
            {
                if (File.Exists(outputPath))
                {
                    File.Delete(outputPath);
                }

                File.Move(encodeResult.OutputPath, outputPath, true);
            }

            await UpdateTrackVideoGenerationAsync(
                track,
                candidate,
                profile,
                configure: item =>
                {
                    item.Status = "completed";
                    item.ProgressCurrentFrame = analysis.FrameCount;
                    item.ProgressTotalFrames = analysis.FrameCount;
                    item.ProgressPercent = 100;
                    item.TrackDurationSeconds = duration;
                    item.OutputVideoPath = outputPath;
                    item.AnalysisPath = analysisPath;
                    item.FfmpegCommand = encodeResult.CommandLine;
                    item.ErrorMessage = null;
                    item.FinishedAtUtc = DateTimeOffset.UtcNow;
                    item.UpdatedAtUtc = DateTimeOffset.UtcNow;
                });

            return MediaExecutionResult.Ok(outputPath);
        }
        catch (Exception ex)
        {
            await UpdateTrackVideoGenerationAsync(
                track,
                candidate,
                profile,
                configure: item =>
                {
                    item.Status = "failed";
                    item.ErrorMessage = ex.Message;
                    item.FinishedAtUtc = DateTimeOffset.UtcNow;
                    item.UpdatedAtUtc = DateTimeOffset.UtcNow;
                });
            return MediaExecutionResult.Fail(ex.Message);
        }
        finally
        {
            if (job is not null)
            {
                workingDirectoryService.TryCleanup(job);
            }
        }
    }

    private async Task UpdateTrackVideoGenerationAsync(
        Track track,
        RenderCandidate candidate,
        LocalMediaRenderProfile profile,
        Action<TrackVideoGeneration> configure)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<YtProducerDbContext>();
        var item = await dbContext.TrackVideoGenerations
            .FirstOrDefaultAsync(x => x.TrackId == track.Id);

        if (item == null)
        {
            item = new TrackVideoGeneration
            {
                Id = Guid.NewGuid(),
                TrackId = track.Id,
                PlaylistId = track.PlaylistId,
                PlaylistPosition = track.PlaylistPosition,
                Status = "pending",
                ProgressPercent = 0,
                ImagePath = candidate.ImagePath,
                AudioPath = candidate.AudioPath,
                Width = profile.Width,
                Height = profile.Height,
                Fps = profile.Fps,
                EqBands = profile.EqBands,
                VideoBitrate = profile.VideoBitrate,
                AudioBitrate = profile.AudioBitrate,
                Seed = profile.Seed,
                UseGpu = profile.UseGpu,
                KeepTemp = false,
                UseRawPipe = profile.UseRawPipe,
                RendererVariant = profile.RendererVariant,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };
            dbContext.TrackVideoGenerations.Add(item);
        }

        configure(item);
        await dbContext.SaveChangesAsync();
    }

    private static TestGenerateVideoProfile? ResolveTestGenerateVideoProfile(string version)
    {
        if (string.Equals(version, "v1", StringComparison.OrdinalIgnoreCase))
        {
            return new TestGenerateVideoProfile(
                ImagePath: "/Volumes/SS2TBSND/YtMusicProducer/MediaTst/10-sec-music.jpg",
                AudioPath: "/Volumes/SS2TBSND/YtMusicProducer/MediaTst/10-sec-music.mp3",
                TempDir: "/Volumes/SS2TBSND/YtMusicProducer/MediaTst/",
                OutputDir: "/Volumes/SS2TBSND/YtMusicProducer/MediaTst/",
                Width: 1920,
                Height: 1080,
                Fps: 24,
                EqBands: 64,
                VideoBitrate: "12M",
                AudioBitrate: "320k",
                Seed: 42,
                UseGpu: true,
                KeepTemp: false,
                UseRawPipe: false,
                RendererVariant: "default",
                OutputFileNameOverride: null,
                LogoPath: null);
        }

        if (string.Equals(version, "v2", StringComparison.OrdinalIgnoreCase))
        {
            return new TestGenerateVideoProfile(
                ImagePath: "/Volumes/SS2TBSND/YtMusicProducer/MediaTst/10-sec-music.jpg",
                AudioPath: "/Volumes/SS2TBSND/YtMusicProducer/MediaTst/10-sec-music.mp3",
                TempDir: "/Volumes/SS2TBSND/YtMusicProducer/MediaTst/",
                OutputDir: "/Volumes/SS2TBSND/YtMusicProducer/MediaTst/",
                Width: 1920,
                Height: 1080,
                Fps: 24,
                EqBands: 32,
                VideoBitrate: "12M",
                AudioBitrate: "320k",
                Seed: 42,
                UseGpu: true,
                KeepTemp: false,
                UseRawPipe: false,
                RendererVariant: "default",
                OutputFileNameOverride: null,
                LogoPath: null);
        }

        if (string.Equals(version, "v3", StringComparison.OrdinalIgnoreCase))
        {
            return new TestGenerateVideoProfile(
                ImagePath: "/Volumes/SS2TBSND/YtMusicProducer/MediaTst/10-sec-music.jpg",
                AudioPath: "/Volumes/SS2TBSND/YtMusicProducer/MediaTst/10-sec-music.mp3",
                TempDir: "/Volumes/SS2TBSND/YtMusicProducer/MediaTst/",
                OutputDir: "/Volumes/SS2TBSND/YtMusicProducer/MediaTst/",
                Width: 1920,
                Height: 1080,
                Fps: 24,
                EqBands: 64,
                VideoBitrate: "12M",
                AudioBitrate: "320k",
                Seed: 42,
                UseGpu: true,
                KeepTemp: false,
                UseRawPipe: true,
                RendererVariant: "default",
                OutputFileNameOverride: null,
                LogoPath: null);
        }

        if (string.Equals(version, "v4", StringComparison.OrdinalIgnoreCase))
        {
            return new TestGenerateVideoProfile(
                ImagePath: "/Volumes/SS2TBSND/YtMusicProducer/MediaTst/5-sec-music.jpg",
                AudioPath: "/Volumes/SS2TBSND/YtMusicProducer/MediaTst/5-sec-music.mp3",
                TempDir: "/Volumes/SS2TBSND/YtMusicProducer/MediaTst/",
                OutputDir: "/Volumes/SS2TBSND/YtMusicProducer/MediaTst/",
                Width: 1920,
                Height: 1080,
                Fps: 24,
                EqBands: 64,
                VideoBitrate: "12M",
                AudioBitrate: "320k",
                Seed: 42,
                UseGpu: true,
                KeepTemp: false,
                UseRawPipe: true,
                RendererVariant: "v4",
                OutputFileNameOverride: "5-sec-music.mp4",
                LogoPath: null);
        }

        if (string.Equals(version, "v5", StringComparison.OrdinalIgnoreCase))
        {
            return new TestGenerateVideoProfile(
                ImagePath: "/Volumes/SS2TBSND/YtMusicProducer/MediaTst/5-sec-music.jpg",
                AudioPath: "/Volumes/SS2TBSND/YtMusicProducer/MediaTst/5-sec-music.mp3",
                TempDir: "/Volumes/SS2TBSND/YtMusicProducer/MediaTst/",
                OutputDir: "/Volumes/SS2TBSND/YtMusicProducer/MediaTst/",
                Width: 1920,
                Height: 1080,
                Fps: 24,
                EqBands: 64,
                VideoBitrate: "12M",
                AudioBitrate: "320k",
                Seed: 42,
                UseGpu: true,
                KeepTemp: false,
                UseRawPipe: true,
                RendererVariant: "v5",
                OutputFileNameOverride: "5-sec-music.mp4",
                LogoPath: null);
        }

        if (string.Equals(version, "v6", StringComparison.OrdinalIgnoreCase))
        {
            return new TestGenerateVideoProfile(
                ImagePath: "/Volumes/SS2TBSND/YtMusicProducer/MediaTst/5-sec-music.jpg",
                AudioPath: "/Volumes/SS2TBSND/YtMusicProducer/MediaTst/5-sec-music.mp3",
                TempDir: "/Volumes/SS2TBSND/YtMusicProducer/MediaTst/",
                OutputDir: "/Volumes/SS2TBSND/YtMusicProducer/MediaTst/",
                Width: 1920,
                Height: 1080,
                Fps: 24,
                EqBands: 64,
                VideoBitrate: "12M",
                AudioBitrate: "320k",
                Seed: 42,
                UseGpu: true,
                KeepTemp: false,
                UseRawPipe: true,
                RendererVariant: "v6",
                OutputFileNameOverride: "5-sec-music.mp4",
                LogoPath: "/Volumes/SS2TBSND/YtMusicProducer/WorkingDirectory/AuruzMusic_logo-1.png");
        }

        if (string.Equals(version, "v7", StringComparison.OrdinalIgnoreCase))
        {
            return new TestGenerateVideoProfile(
                ImagePath: "/Volumes/SS2TBSND/YtMusicProducer/MediaTst/10-sec-music.jpg",
                AudioPath: "/Volumes/SS2TBSND/YtMusicProducer/MediaTst/10-sec-music.mp3",
                TempDir: "/Volumes/SS2TBSND/YtMusicProducer/MediaTst/",
                OutputDir: "/Volumes/SS2TBSND/YtMusicProducer/MediaTst/",
                Width: 1920,
                Height: 1080,
                Fps: 20,
                EqBands: 48,
                VideoBitrate: "10M",
                AudioBitrate: "256k",
                Seed: 42,
                UseGpu: true,
                KeepTemp: false,
                UseRawPipe: true,
                RendererVariant: "v7",
                OutputFileNameOverride: "10-sec-music.mp4",
                LogoPath: "/Volumes/SS2TBSND/YtMusicProducer/WorkingDirectory/AuruzMusic_logo-1.png");
        }

        return null;
    }

    private static LocalMediaRenderProfile? ResolveLocalMediaRenderProfile(string? name)
    {
        var value = string.IsNullOrWhiteSpace(name) ? "quality" : name.Trim().ToLowerInvariant();
        var logoPath = Environment.GetEnvironmentVariable("YT_PRODUCER_THUMBNAIL_LOGO_PATH");

        return value switch
        {
            "legacy" => new LocalMediaRenderProfile("legacy", "default", 1920, 1080, 24, 64, "12M", "320k", 42, true, false, null),
            "quality" => new LocalMediaRenderProfile("quality", "v6", 1920, 1080, 24, 64, "12M", "320k", 42, true, true, logoPath),
            "fast" => new LocalMediaRenderProfile("fast", "v7", 1920, 1080, 20, 48, "10M", "256k", 42, true, true, logoPath),
            _ => null
        };
    }

    private async Task<VideoEncodeResult> EncodeRawFramesWithFallbackAsync(
        FrameRenderService frameRenderService,
        string ffmpegPath,
        string imagePath,
        AnalysisDocument analysis,
        string audioPath,
        int width,
        int height,
        int fps,
        int seed,
        string videoBitrate,
        string audioBitrate,
        string outputDir,
        string logsDir,
        bool useGpu,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputDir);
        Directory.CreateDirectory(logsDir);

        var codecs = GetPreferredVideoCodecs(useGpu);
        VideoEncodeResult? lastResult = null;

        foreach (var codec in codecs)
        {
            var outputPath = Path.Combine(
                outputDir,
                $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..8]}.mp4");
            var stderrPath = Path.Combine(logsDir, $"ffmpeg_stderr_raw_{codec}.txt");

            var args = new List<string>
            {
                "-y",
                "-f", "rawvideo",
                "-pix_fmt", "rgba",
                "-s", $"{width}x{height}",
                "-r", fps.ToString(),
                "-i", "-",
                "-i", audioPath,
                "-c:v", codec
            };

            if (string.Equals(codec, "libx264", StringComparison.Ordinal))
            {
                args.Add("-profile:v");
                args.Add("high");
            }

            args.Add("-pix_fmt");
            args.Add("yuv420p");
            args.Add("-r");
            args.Add(fps.ToString());
            args.Add("-b:v");
            args.Add(videoBitrate);
            args.Add("-movflags");
            args.Add("+faststart");
            args.Add("-c:a");
            args.Add("aac");
            args.Add("-b:a");
            args.Add(audioBitrate);
            args.Add("-ar");
            args.Add("48000");
            args.Add("-shortest");
            args.Add(outputPath);

            var startInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = outputDir
            };

            foreach (var arg in args)
            {
                startInfo.ArgumentList.Add(arg);
            }

            using var process = new Process { StartInfo = startInfo };
            if (!process.Start())
            {
                lastResult = new VideoEncodeResult(
                    false,
                    outputPath,
                    FfmpegRunner.BuildCommandLine(ffmpegPath, args),
                    $"Failed to start ffmpeg for codec {codec}.",
                    stderrPath);
                continue;
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

            try
            {
                await frameRenderService.RenderFramesToRawStreamAsync(
                    imagePath,
                    analysis,
                    width,
                    height,
                    seed,
                    process.StandardInput.BaseStream,
                    cancellationToken);
                await process.StandardInput.FlushAsync(cancellationToken);
            }
            finally
            {
                process.StandardInput.Close();
            }

            await process.WaitForExitAsync(cancellationToken);
            var _ = await stdoutTask;
            var stderr = await stderrTask;
            await File.WriteAllTextAsync(stderrPath, stderr, cancellationToken);

            if (process.ExitCode == 0 && File.Exists(outputPath))
            {
                return new VideoEncodeResult(
                    true,
                    outputPath,
                    FfmpegRunner.BuildCommandLine(ffmpegPath, args),
                    ClipTail(stderr, 8000),
                    stderrPath);
            }

            lastResult = new VideoEncodeResult(
                false,
                outputPath,
                FfmpegRunner.BuildCommandLine(ffmpegPath, args),
                ClipTail(stderr, 8000),
                stderrPath);
        }

        return lastResult ?? new VideoEncodeResult(
            false,
            string.Empty,
            string.Empty,
            "No codec attempts executed for raw pipe encode.",
            Path.Combine(logsDir, "ffmpeg_stderr_raw_none.txt"));
    }

    private async Task<VideoEncodeResult> EncodeRawFramesV4WithFallbackAsync(
        FrameRenderServiceV4 frameRenderService,
        string ffmpegPath,
        string imagePath,
        AnalysisDocument analysis,
        string audioPath,
        int width,
        int height,
        int fps,
        int seed,
        string videoBitrate,
        string audioBitrate,
        string outputDir,
        string? outputFileNameOverride,
        string logsDir,
        bool useGpu,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputDir);
        Directory.CreateDirectory(logsDir);

        var codecs = GetPreferredVideoCodecs(useGpu);
        VideoEncodeResult? lastResult = null;

        foreach (var codec in codecs)
        {
            var outputPath = !string.IsNullOrWhiteSpace(outputFileNameOverride)
                ? Path.Combine(outputDir, outputFileNameOverride)
                : Path.Combine(outputDir, $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..8]}.mp4");

            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }

            var stderrPath = Path.Combine(logsDir, $"ffmpeg_stderr_raw_{codec}.txt");

            var args = new List<string>
            {
                "-y",
                "-f", "rawvideo",
                "-pix_fmt", "rgba",
                "-s", $"{width}x{height}",
                "-r", fps.ToString(),
                "-i", "-",
                "-i", audioPath,
                "-c:v", codec
            };

            if (string.Equals(codec, "libx264", StringComparison.Ordinal))
            {
                args.Add("-profile:v");
                args.Add("high");
            }

            args.Add("-pix_fmt");
            args.Add("yuv420p");
            args.Add("-r");
            args.Add(fps.ToString());
            args.Add("-b:v");
            args.Add(videoBitrate);
            args.Add("-movflags");
            args.Add("+faststart");
            args.Add("-c:a");
            args.Add("aac");
            args.Add("-b:a");
            args.Add(audioBitrate);
            args.Add("-ar");
            args.Add("48000");
            args.Add("-shortest");
            args.Add(outputPath);

            var startInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = outputDir
            };

            foreach (var arg in args)
            {
                startInfo.ArgumentList.Add(arg);
            }

            using var process = new Process { StartInfo = startInfo };
            if (!process.Start())
            {
                lastResult = new VideoEncodeResult(
                    false,
                    outputPath,
                    FfmpegRunner.BuildCommandLine(ffmpegPath, args),
                    $"Failed to start ffmpeg for codec {codec}.",
                    stderrPath);
                continue;
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

            try
            {
                await frameRenderService.RenderFramesToRawStreamAsync(
                    imagePath,
                    analysis,
                    width,
                    height,
                    seed,
                    process.StandardInput.BaseStream,
                    cancellationToken);
                await process.StandardInput.FlushAsync(cancellationToken);
            }
            finally
            {
                process.StandardInput.Close();
            }

            await process.WaitForExitAsync(cancellationToken);
            var _ = await stdoutTask;
            var stderr = await stderrTask;
            await File.WriteAllTextAsync(stderrPath, stderr, cancellationToken);

            if (process.ExitCode == 0 && File.Exists(outputPath))
            {
                return new VideoEncodeResult(
                    true,
                    outputPath,
                    FfmpegRunner.BuildCommandLine(ffmpegPath, args),
                    ClipTail(stderr, 8000),
                    stderrPath);
            }

            lastResult = new VideoEncodeResult(
                false,
                outputPath,
                FfmpegRunner.BuildCommandLine(ffmpegPath, args),
                ClipTail(stderr, 8000),
                stderrPath);
        }

        return lastResult ?? new VideoEncodeResult(
            false,
            string.Empty,
            string.Empty,
            "No codec attempts executed for v4 raw pipe encode.",
            Path.Combine(logsDir, "ffmpeg_stderr_raw_none.txt"));
    }

    private async Task<VideoEncodeResult> EncodeRawFramesV5WithFallbackAsync(
        FrameRenderServiceV5 frameRenderService,
        string ffmpegPath,
        string imagePath,
        AnalysisDocument analysis,
        string audioPath,
        int width,
        int height,
        int fps,
        int seed,
        string videoBitrate,
        string audioBitrate,
        string outputDir,
        string? outputFileNameOverride,
        string logsDir,
        bool useGpu,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputDir);
        Directory.CreateDirectory(logsDir);

        var codecs = GetPreferredVideoCodecs(useGpu);
        VideoEncodeResult? lastResult = null;
        var progressState = new ProgressFileState();
        var processBaseName = $"{Path.GetFileNameWithoutExtension(audioPath)}_process";

        foreach (var codec in codecs)
        {
            var outputPath = !string.IsNullOrWhiteSpace(outputFileNameOverride)
                ? Path.Combine(outputDir, outputFileNameOverride)
                : Path.Combine(outputDir, $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..8]}.mp4");

            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }

            var stderrPath = Path.Combine(logsDir, $"ffmpeg_stderr_raw_{codec}.txt");

            var args = new List<string>
            {
                "-y",
                "-f", "rawvideo",
                "-pix_fmt", "rgba",
                "-s", $"{width}x{height}",
                "-r", fps.ToString(),
                "-i", "-",
                "-i", audioPath,
                "-c:v", codec
            };

            if (string.Equals(codec, "libx264", StringComparison.Ordinal))
            {
                args.Add("-profile:v");
                args.Add("high");
            }

            args.Add("-pix_fmt");
            args.Add("yuv420p");
            args.Add("-r");
            args.Add(fps.ToString());
            args.Add("-b:v");
            args.Add(videoBitrate);
            args.Add("-movflags");
            args.Add("+faststart");
            args.Add("-c:a");
            args.Add("aac");
            args.Add("-b:a");
            args.Add(audioBitrate);
            args.Add("-ar");
            args.Add("48000");
            args.Add("-shortest");
            args.Add(outputPath);

            var startInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = outputDir
            };

            foreach (var arg in args)
            {
                startInfo.ArgumentList.Add(arg);
            }

            using var process = new Process { StartInfo = startInfo };
            if (!process.Start())
            {
                lastResult = new VideoEncodeResult(
                    false,
                    outputPath,
                    FfmpegRunner.BuildCommandLine(ffmpegPath, args),
                    $"Failed to start ffmpeg for codec {codec}.",
                    stderrPath);
                continue;
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

            var lastProgressUpdateUtc = DateTimeOffset.MinValue;
            Func<int, int, Task> onProgressAsync = (current, total) =>
            {
                var now = DateTimeOffset.UtcNow;
                if (current < total && (now - lastProgressUpdateUtc).TotalMilliseconds < 250)
                {
                    return Task.CompletedTask;
                }

                lastProgressUpdateUtc = now;
                UpdateProgressFile(outputDir, processBaseName, current, total, progressState);
                return Task.CompletedTask;
            };

            try
            {
                await frameRenderService.RenderFramesToRawStreamAsync(
                    imagePath,
                    analysis,
                    width,
                    height,
                    seed,
                    process.StandardInput.BaseStream,
                    onProgressAsync,
                    cancellationToken);
                await process.StandardInput.FlushAsync(cancellationToken);
            }
            finally
            {
                process.StandardInput.Close();
            }

            await process.WaitForExitAsync(cancellationToken);
            var _ = await stdoutTask;
            var stderr = await stderrTask;
            await File.WriteAllTextAsync(stderrPath, stderr, cancellationToken);

            if (process.ExitCode == 0 && File.Exists(outputPath))
            {
                UpdateProgressFile(outputDir, processBaseName, analysis.FrameCount, analysis.FrameCount, progressState);
                return new VideoEncodeResult(
                    true,
                    outputPath,
                    FfmpegRunner.BuildCommandLine(ffmpegPath, args),
                    ClipTail(stderr, 8000),
                    stderrPath);
            }

            lastResult = new VideoEncodeResult(
                false,
                outputPath,
                FfmpegRunner.BuildCommandLine(ffmpegPath, args),
                ClipTail(stderr, 8000),
                stderrPath);
        }

        return lastResult ?? new VideoEncodeResult(
            false,
            string.Empty,
            string.Empty,
            "No codec attempts executed for v5 raw pipe encode.",
            Path.Combine(logsDir, "ffmpeg_stderr_raw_none.txt"));
    }

    private async Task<VideoEncodeResult> EncodeRawFramesV6WithFallbackAsync(
        FrameRenderServiceV6 frameRenderService,
        string ffmpegPath,
        string imagePath,
        string? logoPath,
        AnalysisDocument analysis,
        string audioPath,
        int width,
        int height,
        int fps,
        int seed,
        string videoBitrate,
        string audioBitrate,
        string outputDir,
        string? outputFileNameOverride,
        string logsDir,
        bool useGpu,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(logoPath))
        {
            return new VideoEncodeResult(false, string.Empty, string.Empty, "Logo path is missing for v6.", string.Empty);
        }

        if (!File.Exists(logoPath))
        {
            return new VideoEncodeResult(false, string.Empty, string.Empty, $"Logo file not found: {logoPath}", string.Empty);
        }

        Directory.CreateDirectory(outputDir);
        Directory.CreateDirectory(logsDir);

        var codecs = GetPreferredVideoCodecs(useGpu);
        VideoEncodeResult? lastResult = null;
        var progressState = new ProgressFileState();
        var processBaseName = $"{Path.GetFileNameWithoutExtension(audioPath)}_process";

        foreach (var codec in codecs)
        {
            var outputPath = !string.IsNullOrWhiteSpace(outputFileNameOverride)
                ? Path.Combine(outputDir, outputFileNameOverride)
                : Path.Combine(outputDir, $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..8]}.mp4");

            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }

            var stderrPath = Path.Combine(logsDir, $"ffmpeg_stderr_raw_{codec}.txt");

            var args = new List<string>
            {
                "-y",
                "-f", "rawvideo",
                "-pix_fmt", "rgba",
                "-s", $"{width}x{height}",
                "-r", fps.ToString(),
                "-i", "-",
                "-i", audioPath,
                "-c:v", codec
            };

            if (string.Equals(codec, "libx264", StringComparison.Ordinal))
            {
                args.Add("-profile:v");
                args.Add("high");
            }

            args.Add("-pix_fmt");
            args.Add("yuv420p");
            args.Add("-r");
            args.Add(fps.ToString());
            args.Add("-b:v");
            args.Add(videoBitrate);
            args.Add("-movflags");
            args.Add("+faststart");
            args.Add("-c:a");
            args.Add("aac");
            args.Add("-b:a");
            args.Add(audioBitrate);
            args.Add("-ar");
            args.Add("48000");
            args.Add("-shortest");
            args.Add(outputPath);

            var startInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = outputDir
            };

            foreach (var arg in args)
            {
                startInfo.ArgumentList.Add(arg);
            }

            using var process = new Process { StartInfo = startInfo };
            if (!process.Start())
            {
                lastResult = new VideoEncodeResult(
                    false,
                    outputPath,
                    FfmpegRunner.BuildCommandLine(ffmpegPath, args),
                    $"Failed to start ffmpeg for codec {codec}.",
                    stderrPath);
                continue;
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

            var lastProgressUpdateUtc = DateTimeOffset.MinValue;
            Func<int, int, Task> onProgressAsync = (current, total) =>
            {
                var now = DateTimeOffset.UtcNow;
                if (current < total && (now - lastProgressUpdateUtc).TotalMilliseconds < 250)
                {
                    return Task.CompletedTask;
                }

                lastProgressUpdateUtc = now;
                UpdateProgressFile(outputDir, processBaseName, current, total, progressState);
                return Task.CompletedTask;
            };

            try
            {
                await frameRenderService.RenderFramesToRawStreamAsync(
                    imagePath,
                    logoPath,
                    analysis,
                    width,
                    height,
                    seed,
                    process.StandardInput.BaseStream,
                    onProgressAsync,
                    cancellationToken);
                await process.StandardInput.FlushAsync(cancellationToken);
            }
            finally
            {
                process.StandardInput.Close();
            }

            await process.WaitForExitAsync(cancellationToken);
            var _ = await stdoutTask;
            var stderr = await stderrTask;
            await File.WriteAllTextAsync(stderrPath, stderr, cancellationToken);

            if (process.ExitCode == 0 && File.Exists(outputPath))
            {
                UpdateProgressFile(outputDir, processBaseName, analysis.FrameCount, analysis.FrameCount, progressState);
                return new VideoEncodeResult(
                    true,
                    outputPath,
                    FfmpegRunner.BuildCommandLine(ffmpegPath, args),
                    ClipTail(stderr, 8000),
                    stderrPath);
            }

            lastResult = new VideoEncodeResult(
                false,
                outputPath,
                FfmpegRunner.BuildCommandLine(ffmpegPath, args),
                ClipTail(stderr, 8000),
                stderrPath);
        }

        return lastResult ?? new VideoEncodeResult(
            false,
            string.Empty,
            string.Empty,
            "No codec attempts executed for v6 raw pipe encode.",
            Path.Combine(logsDir, "ffmpeg_stderr_raw_none.txt"));
    }

    private async Task<VideoEncodeResult> EncodeRawFramesV7WithFallbackAsync(
        FrameRenderServiceV6 frameRenderService,
        string ffmpegPath,
        string imagePath,
        string? logoPath,
        AnalysisDocument analysis,
        string audioPath,
        int outputWidth,
        int outputHeight,
        int fps,
        int seed,
        string videoBitrate,
        string audioBitrate,
        string outputDir,
        string? outputFileNameOverride,
        string logsDir,
        bool useGpu,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(logoPath))
        {
            return new VideoEncodeResult(false, string.Empty, string.Empty, "Logo path is missing for v7.", string.Empty);
        }

        if (!File.Exists(logoPath))
        {
            return new VideoEncodeResult(false, string.Empty, string.Empty, $"Logo file not found: {logoPath}", string.Empty);
        }

        Directory.CreateDirectory(outputDir);
        Directory.CreateDirectory(logsDir);

        var renderWidth = Math.Max(640, (int)Math.Round(outputWidth / 1.5));
        var renderHeight = Math.Max(360, (int)Math.Round(outputHeight / 1.5));
        if ((renderWidth & 1) == 1) renderWidth--;
        if ((renderHeight & 1) == 1) renderHeight--;

        var codecs = GetPreferredVideoCodecs(useGpu);
        VideoEncodeResult? lastResult = null;
        var progressState = new ProgressFileState();
        var processBaseName = $"{Path.GetFileNameWithoutExtension(audioPath)}_process";

        foreach (var codec in codecs)
        {
            var outputPath = !string.IsNullOrWhiteSpace(outputFileNameOverride)
                ? Path.Combine(outputDir, outputFileNameOverride)
                : Path.Combine(outputDir, $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..8]}.mp4");

            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }

            var stderrPath = Path.Combine(logsDir, $"ffmpeg_stderr_raw_{codec}.txt");

            var args = new List<string>
            {
                "-y",
                "-f", "rawvideo",
                "-pix_fmt", "rgba",
                "-s", $"{renderWidth}x{renderHeight}",
                "-r", fps.ToString(),
                "-i", "-",
                "-i", audioPath,
                "-vf", $"scale={outputWidth}:{outputHeight}:flags=fast_bilinear",
                "-c:v", codec
            };

            if (string.Equals(codec, "libx264", StringComparison.Ordinal))
            {
                args.Add("-profile:v");
                args.Add("high");
            }

            args.Add("-pix_fmt");
            args.Add("yuv420p");
            args.Add("-r");
            args.Add(fps.ToString());
            args.Add("-b:v");
            args.Add(videoBitrate);
            args.Add("-movflags");
            args.Add("+faststart");
            args.Add("-c:a");
            args.Add("aac");
            args.Add("-b:a");
            args.Add(audioBitrate);
            args.Add("-ar");
            args.Add("48000");
            args.Add("-shortest");
            args.Add(outputPath);

            var startInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = outputDir
            };

            foreach (var arg in args)
            {
                startInfo.ArgumentList.Add(arg);
            }

            using var process = new Process { StartInfo = startInfo };
            if (!process.Start())
            {
                lastResult = new VideoEncodeResult(
                    false,
                    outputPath,
                    FfmpegRunner.BuildCommandLine(ffmpegPath, args),
                    $"Failed to start ffmpeg for codec {codec}.",
                    stderrPath);
                continue;
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

            var lastProgressUpdateUtc = DateTimeOffset.MinValue;
            Func<int, int, Task> onProgressAsync = (current, total) =>
            {
                var now = DateTimeOffset.UtcNow;
                if (current < total && (now - lastProgressUpdateUtc).TotalMilliseconds < 250)
                {
                    return Task.CompletedTask;
                }

                lastProgressUpdateUtc = now;
                UpdateProgressFile(outputDir, processBaseName, current, total, progressState);
                return Task.CompletedTask;
            };

            try
            {
                await frameRenderService.RenderFramesToRawStreamAsync(
                    imagePath,
                    logoPath,
                    analysis,
                    renderWidth,
                    renderHeight,
                    seed,
                    process.StandardInput.BaseStream,
                    onProgressAsync,
                    cancellationToken);
                await process.StandardInput.FlushAsync(cancellationToken);
            }
            finally
            {
                process.StandardInput.Close();
            }

            await process.WaitForExitAsync(cancellationToken);
            var _ = await stdoutTask;
            var stderr = await stderrTask;
            await File.WriteAllTextAsync(stderrPath, stderr, cancellationToken);

            if (process.ExitCode == 0 && File.Exists(outputPath))
            {
                UpdateProgressFile(outputDir, processBaseName, analysis.FrameCount, analysis.FrameCount, progressState);
                return new VideoEncodeResult(
                    true,
                    outputPath,
                    FfmpegRunner.BuildCommandLine(ffmpegPath, args),
                    ClipTail(stderr, 8000),
                    stderrPath);
            }

            lastResult = new VideoEncodeResult(
                false,
                outputPath,
                FfmpegRunner.BuildCommandLine(ffmpegPath, args),
                ClipTail(stderr, 8000),
                stderrPath);
        }

        return lastResult ?? new VideoEncodeResult(
            false,
            string.Empty,
            string.Empty,
            "No codec attempts executed for v7 raw pipe encode.",
            Path.Combine(logsDir, "ffmpeg_stderr_raw_none.txt"));
    }

    private async Task<VideoEncodeResult> EncodeRawFramesV6WithoutProgressFileAsync(
        FrameRenderServiceV6 frameRenderService,
        string ffmpegPath,
        string imagePath,
        string? logoPath,
        AnalysisDocument analysis,
        string audioPath,
        int width,
        int height,
        int fps,
        int seed,
        string videoBitrate,
        string audioBitrate,
        string outputDir,
        string? outputFileNameOverride,
        string logsDir,
        bool useGpu,
        Func<int, int, Task>? onProgressAsync,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(logoPath))
        {
            return new VideoEncodeResult(false, string.Empty, string.Empty, "Logo path is missing for v6.", string.Empty);
        }

        if (!File.Exists(logoPath))
        {
            return new VideoEncodeResult(false, string.Empty, string.Empty, $"Logo file not found: {logoPath}", string.Empty);
        }

        Directory.CreateDirectory(outputDir);
        Directory.CreateDirectory(logsDir);

        var codecs = GetPreferredVideoCodecs(useGpu);
        VideoEncodeResult? lastResult = null;

        foreach (var codec in codecs)
        {
            var outputPath = !string.IsNullOrWhiteSpace(outputFileNameOverride)
                ? Path.Combine(outputDir, outputFileNameOverride)
                : Path.Combine(outputDir, $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..8]}.mp4");

            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }

            var stderrPath = Path.Combine(logsDir, $"ffmpeg_stderr_raw_{codec}.txt");
            var args = BuildRawVideoEncodeArgs(codec, audioPath, outputPath, width, height, fps, videoBitrate, audioBitrate);
            var startInfo = CreateRawVideoEncodeStartInfo(ffmpegPath, outputDir, args);

            using var process = new Process { StartInfo = startInfo };
            if (!process.Start())
            {
                lastResult = new VideoEncodeResult(false, outputPath, FfmpegRunner.BuildCommandLine(ffmpegPath, args), $"Failed to start ffmpeg for codec {codec}.", stderrPath);
                continue;
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

            try
            {
                await frameRenderService.RenderFramesToRawStreamAsync(
                    imagePath,
                    logoPath,
                    analysis,
                    width,
                    height,
                    seed,
                    process.StandardInput.BaseStream,
                    onProgressAsync,
                    cancellationToken);
                await process.StandardInput.FlushAsync(cancellationToken);
            }
            finally
            {
                process.StandardInput.Close();
            }

            await process.WaitForExitAsync(cancellationToken);
            var _ = await stdoutTask;
            var stderr = await stderrTask;
            await File.WriteAllTextAsync(stderrPath, stderr, cancellationToken);

            if (process.ExitCode == 0 && File.Exists(outputPath))
            {
                if (onProgressAsync is not null)
                {
                    await onProgressAsync(analysis.FrameCount, analysis.FrameCount);
                }

                return new VideoEncodeResult(true, outputPath, FfmpegRunner.BuildCommandLine(ffmpegPath, args), ClipTail(stderr, 8000), stderrPath);
            }

            lastResult = new VideoEncodeResult(false, outputPath, FfmpegRunner.BuildCommandLine(ffmpegPath, args), ClipTail(stderr, 8000), stderrPath);
        }

        return lastResult ?? new VideoEncodeResult(false, string.Empty, string.Empty, "No codec attempts executed for v6 raw pipe encode.", Path.Combine(logsDir, "ffmpeg_stderr_raw_none.txt"));
    }

    private async Task<VideoEncodeResult> EncodeRawFramesV7WithoutProgressFileAsync(
        FrameRenderServiceV6 frameRenderService,
        string ffmpegPath,
        string imagePath,
        string? logoPath,
        AnalysisDocument analysis,
        string audioPath,
        int outputWidth,
        int outputHeight,
        int fps,
        int seed,
        string videoBitrate,
        string audioBitrate,
        string outputDir,
        string? outputFileNameOverride,
        string logsDir,
        bool useGpu,
        Func<int, int, Task>? onProgressAsync,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(logoPath))
        {
            return new VideoEncodeResult(false, string.Empty, string.Empty, "Logo path is missing for v7.", string.Empty);
        }

        if (!File.Exists(logoPath))
        {
            return new VideoEncodeResult(false, string.Empty, string.Empty, $"Logo file not found: {logoPath}", string.Empty);
        }

        Directory.CreateDirectory(outputDir);
        Directory.CreateDirectory(logsDir);

        var renderWidth = Math.Max(640, (int)Math.Round(outputWidth / 1.5));
        var renderHeight = Math.Max(360, (int)Math.Round(outputHeight / 1.5));
        if ((renderWidth & 1) == 1) renderWidth--;
        if ((renderHeight & 1) == 1) renderHeight--;

        var codecs = GetPreferredVideoCodecs(useGpu);
        VideoEncodeResult? lastResult = null;

        foreach (var codec in codecs)
        {
            var outputPath = !string.IsNullOrWhiteSpace(outputFileNameOverride)
                ? Path.Combine(outputDir, outputFileNameOverride)
                : Path.Combine(outputDir, $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..8]}.mp4");

            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }

            var stderrPath = Path.Combine(logsDir, $"ffmpeg_stderr_raw_{codec}.txt");
            var args = BuildRawVideoEncodeArgs(codec, audioPath, outputPath, renderWidth, renderHeight, fps, videoBitrate, audioBitrate);
            var codecIndex = args.FindIndex(x => string.Equals(x, "-c:v", StringComparison.Ordinal));
            if (codecIndex < 0)
            {
                codecIndex = args.Count;
            }

            args.InsertRange(codecIndex, ["-vf", $"scale={outputWidth}:{outputHeight}:flags=fast_bilinear"]);
            var startInfo = CreateRawVideoEncodeStartInfo(ffmpegPath, outputDir, args);

            using var process = new Process { StartInfo = startInfo };
            if (!process.Start())
            {
                lastResult = new VideoEncodeResult(false, outputPath, FfmpegRunner.BuildCommandLine(ffmpegPath, args), $"Failed to start ffmpeg for codec {codec}.", stderrPath);
                continue;
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

            try
            {
                await frameRenderService.RenderFramesToRawStreamAsync(
                    imagePath,
                    logoPath,
                    analysis,
                    renderWidth,
                    renderHeight,
                    seed,
                    process.StandardInput.BaseStream,
                    onProgressAsync,
                    cancellationToken);
                await process.StandardInput.FlushAsync(cancellationToken);
            }
            finally
            {
                process.StandardInput.Close();
            }

            await process.WaitForExitAsync(cancellationToken);
            var _ = await stdoutTask;
            var stderr = await stderrTask;
            await File.WriteAllTextAsync(stderrPath, stderr, cancellationToken);

            if (process.ExitCode == 0 && File.Exists(outputPath))
            {
                if (onProgressAsync is not null)
                {
                    await onProgressAsync(analysis.FrameCount, analysis.FrameCount);
                }

                return new VideoEncodeResult(true, outputPath, FfmpegRunner.BuildCommandLine(ffmpegPath, args), ClipTail(stderr, 8000), stderrPath);
            }

            lastResult = new VideoEncodeResult(false, outputPath, FfmpegRunner.BuildCommandLine(ffmpegPath, args), ClipTail(stderr, 8000), stderrPath);
        }

        return lastResult ?? new VideoEncodeResult(false, string.Empty, string.Empty, "No codec attempts executed for v7 raw pipe encode.", Path.Combine(logsDir, "ffmpeg_stderr_raw_none.txt"));
    }

    private static List<string> BuildRawVideoEncodeArgs(
        string codec,
        string audioPath,
        string outputPath,
        int width,
        int height,
        int fps,
        string videoBitrate,
        string audioBitrate)
    {
        var args = new List<string>
        {
            "-y",
            "-f", "rawvideo",
            "-pix_fmt", "rgba",
            "-s", $"{width}x{height}",
            "-r", fps.ToString(),
            "-i", "-",
            "-i", audioPath,
            "-c:v", codec
        };

        if (string.Equals(codec, "libx264", StringComparison.Ordinal))
        {
            args.Add("-profile:v");
            args.Add("high");
        }

        args.Add("-pix_fmt");
        args.Add("yuv420p");
        args.Add("-r");
        args.Add(fps.ToString());
        args.Add("-b:v");
        args.Add(videoBitrate);
        args.Add("-movflags");
        args.Add("+faststart");
        args.Add("-c:a");
        args.Add("aac");
        args.Add("-b:a");
        args.Add(audioBitrate);
        args.Add("-ar");
        args.Add("48000");
        args.Add("-shortest");
        args.Add(outputPath);

        return args;
    }

    private static ProcessStartInfo CreateRawVideoEncodeStartInfo(string ffmpegPath, string outputDir, IEnumerable<string> args)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = outputDir
        };

        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        return startInfo;
    }

    private static void UpdateProgressFile(
        string outputDir,
        string baseName,
        int current,
        int total,
        ProgressFileState state)
    {
        const int width = 30;
        total = Math.Max(1, total);
        current = Math.Clamp(current, 0, total);
        var ratio = current / (double)total;
        var filled = (int)Math.Round(ratio * width);
        filled = Math.Clamp(filled, 0, width);
        var bar = new string('|', filled) + new string(' ', width - filled);
        var fileName = $"{baseName}[{bar}].txt";
        var path = Path.Combine(outputDir, fileName);

        if (string.Equals(state.CurrentPath, path, StringComparison.Ordinal))
        {
            return;
        }

        Directory.CreateDirectory(outputDir);
        File.WriteAllText(path, $"progress {current}/{total} ({ratio:P0}){Environment.NewLine}", Encoding.UTF8);

        if (!string.IsNullOrWhiteSpace(state.CurrentPath) && File.Exists(state.CurrentPath))
        {
            try
            {
                File.Delete(state.CurrentPath);
            }
            catch
            {
                // Best effort.
            }
        }

        state.CurrentPath = path;
    }

    private static IReadOnlyList<string> GetPreferredVideoCodecs(bool useGpu)
    {
        if (!useGpu)
        {
            return ["libx264"];
        }

        if (OperatingSystem.IsMacOS())
        {
            return ["h264_videotoolbox", "libx264"];
        }

        return ["h264_nvenc", "libx264"];
    }

    private static string ClipTail(string? value, int maxChars)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxChars)
        {
            return value ?? string.Empty;
        }

        return value[^maxChars..];
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

    public async Task RunGenerateMusicAsync(string[] commandArgs)
    {
        if (commandArgs.Length < 2)
        {
            global::System.Console.WriteLine("Usage: generate-music <playlistId> <position>");
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

        await GenerateMusicForPositionAsync(playlistId, position);
    }

    public async Task<bool> RunGenerateAllImagesAsync(string[] commandArgs, Guid? jobId = null)
    {
        if (commandArgs.Length < 1)
        {
            global::System.Console.WriteLine("Usage: generate-all-images <playlistId>");
            return false;
        }

        if (!Guid.TryParse(commandArgs[0], out var playlistId))
        {
            global::System.Console.WriteLine("Invalid playlistId.");
            return false;
        }

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
            return false;
        }

        var allSucceeded = true;
        foreach (var position in positions)
        {
            await ThrowIfJobCancelledAsync(jobId);
            var success = await GenerateImageForPositionAsync(playlistId, position, "grok-imagine/text-to-image");
            if (!success)
            {
                allSucceeded = false;
            }

            await ThrowIfJobCancelledAsync(jobId);
        }

        if (allSucceeded)
        {
            await SetPlaylistStatusAsync(playlistId, PlaylistStatus.ImagesGenerated);
        }

        return allSucceeded;
    }

    public async Task RunGenerateAllMusicAsync(string[] commandArgs)
    {
        if (commandArgs.Length < 1)
        {
            global::System.Console.WriteLine("Usage: generate-all-music <playlistId>");
            return;
        }

        if (!Guid.TryParse(commandArgs[0], out var playlistId))
        {
            global::System.Console.WriteLine("Invalid playlistId.");
            return;
        }

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

        var allSucceeded = true;
        foreach (var position in positions)
        {
            var success = await GenerateMusicForPositionAsync(playlistId, position);
            if (!success)
            {
                allSucceeded = false;
            }
        }

        if (allSucceeded)
        {
            await SetPlaylistStatusAsync(playlistId, PlaylistStatus.MusicGenerated);
        }
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

        await UploadYoutubeVideoWithThumbnailForPositionAsync(playlistId, position);
    }

    public async Task RunUploadYoutubeVideosAsync(string[] commandArgs)
    {
        if (commandArgs.Length < 1)
        {
            global::System.Console.WriteLine("Usage: upload-youtube-videos <playlistId>");
            return;
        }

        if (!Guid.TryParse(commandArgs[0], out var playlistId))
        {
            global::System.Console.WriteLine("Invalid playlistId.");
            return;
        }

        var tracks = await _context.Tracks
            .AsNoTracking()
            .Where(t => t.PlaylistId == playlistId)
            .OrderBy(t => t.PlaylistPosition)
            .Select(t => new { t.Id, t.PlaylistPosition })
            .ToListAsync();

        if (tracks.Count == 0)
        {
            global::System.Console.WriteLine("No tracks found for playlist.");
            return;
        }

        var positions = tracks
            .Select(t => t.PlaylistPosition)
            .Distinct()
            .OrderBy(p => p)
            .ToList();

        var uploadedTrackIdList = await _context.TrackOnYoutube
            .AsNoTracking()
            .Where(x => x.PlaylistId == playlistId)
            .Select(x => x.TrackId)
            .ToListAsync();
        var uploadedTrackIds = new HashSet<Guid>(uploadedTrackIdList);

        var publishState = await GetOrCreateYoutubeLastPublishedDateAsync();
        var slotCursor = publishState.LastPublishedDate;
        var scheduledByPosition = new Dictionary<int, DateTimeOffset>();
        foreach (var track in tracks)
        {
            if (uploadedTrackIds.Contains(track.Id))
            {
                continue;
            }

            slotCursor = GetNextYoutubePublishSlotUtc(slotCursor);
            scheduledByPosition[track.PlaylistPosition] = slotCursor;
        }

        var allSucceeded = true;
        foreach (var position in positions)
        {
            var scheduledAt = scheduledByPosition.TryGetValue(position, out var assignedSlot)
                ? assignedSlot
                : (DateTimeOffset?)null;

            var success = await UploadYoutubeVideoWithThumbnailForPositionAsync(playlistId, position, scheduledAt);
            if (!success)
            {
                allSucceeded = false;
            }
        }

        if (allSucceeded)
        {
            await SetPlaylistStatusAsync(playlistId, PlaylistStatus.OnYoutube);
        }
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

            var allSucceeded = true;
            foreach (var trackPosition in positions)
            {
                var success = await CreateYoutubeVideoThumbnailAsync(playlistId, trackPosition);
                if (!success)
                {
                    allSucceeded = false;
                }
            }

            if (allSucceeded)
            {
                await SetPlaylistStatusAsync(playlistId, PlaylistStatus.ThumbnailGenerated);
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

    public async Task RunTrackCreateYoutubeVideoThumbnailV2Async(string[] commandArgs)
    {
        if (commandArgs.Length < 1)
        {
            global::System.Console.WriteLine("Usage: track-create-youtube-video-thumbnail_v2 <playlistId> [position]");
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

            var allSucceeded = true;
            foreach (var trackPosition in positions)
            {
                var success = await CreateYoutubeVideoThumbnailV2Async(playlistId, trackPosition);
                if (!success)
                {
                    allSucceeded = false;
                }
            }

            if (allSucceeded)
            {
                await SetPlaylistStatusAsync(playlistId, PlaylistStatus.ThumbnailGenerated);
            }

            return;
        }

        if (!int.TryParse(commandArgs[1], out var position) || position <= 0)
        {
            global::System.Console.WriteLine("Invalid position. Must be a positive integer.");
            return;
        }

        await CreateYoutubeVideoThumbnailV2Async(playlistId, position);
    }

    private async Task SetPlaylistStatusAsync(Guid playlistId, PlaylistStatus status)
    {
        var playlist = await _context.Playlists.FirstOrDefaultAsync(p => p.Id == playlistId);
        if (playlist == null)
        {
            global::System.Console.WriteLine("Playlist not found.");
            return;
        }

        playlist.Status = status;
        playlist.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await _context.SaveChangesAsync();
        global::System.Console.WriteLine($"playlist status updated playlist={playlistId} status={status}");
    }

    private async Task PrintAllPlaylistsAsync()
    {
        var playlists = await _context.Playlists
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

    private async Task RunPlaylistPipelineAsync(Guid? targetPlaylistId = null)
    {
        var workingDirectory = Environment.GetEnvironmentVariable("YT_PRODUCER_PLAYLIST_WORKING_DIRECTORY");
        if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            _logger.LogWarning("YT_PRODUCER_PLAYLIST_WORKING_DIRECTORY is not configured. Skipping pipeline.");
            return;
        }

        Directory.CreateDirectory(workingDirectory);
        _logger.LogInformation("Pipeline working directory: {WorkingDirectory}", workingDirectory);

        var playlistsQuery = _context.Playlists
            .OrderByDescending(p => p.CreatedAtUtc)
            .AsQueryable();

        if (targetPlaylistId.HasValue)
        {
            playlistsQuery = playlistsQuery.Where(p => p.Id == targetPlaylistId.Value);
        }

        var playlists = await playlistsQuery.ToListAsync();

        if (playlists.Count == 0)
        {
            if (targetPlaylistId.HasValue)
            {
                _logger.LogWarning("Playlist not found: {PlaylistId}", targetPlaylistId.Value);
            }
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

            playlist.Status = PlaylistStatus.FolderCreated;
            playlist.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }

        await _context.SaveChangesAsync();

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

    private async Task<bool> GenerateImageForPositionAsync(Guid playlistId, int position, string? modelOverride)
    {
        var workingDirectory = Environment.GetEnvironmentVariable("YT_PRODUCER_PLAYLIST_WORKING_DIRECTORY");
        if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            global::System.Console.WriteLine("YT_PRODUCER_PLAYLIST_WORKING_DIRECTORY is not configured.");
            return false;
        }

        var mcpWorkingDirectory = Environment.GetEnvironmentVariable("YT_PRODUCER_MCP_KIEAI_WORKING_DIRECTORY");
        if (string.IsNullOrWhiteSpace(mcpWorkingDirectory))
        {
            global::System.Console.WriteLine("YT_PRODUCER_MCP_KIEAI_WORKING_DIRECTORY is not configured.");
            return false;
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
            return false;
        }

        var playlistFolderPath = Path.Combine(workingDirectory, GetPlaylistFolderName(playlist.Id));
        if (!Directory.Exists(playlistFolderPath))
        {
            global::System.Console.WriteLine($"Playlist folder not found: {playlistFolderPath}");
            return false;
        }

        var playlistMetadataPrompts = ParsePromptMetadata(playlist.Metadata);
        var track = playlist.Tracks.FirstOrDefault(t => t.PlaylistPosition == position);
        var trackMetadata = track != null ? ParsePromptMetadata(track.Metadata) : new PromptMetadata(null, null);

        var prompt = trackMetadata.ImagePrompt ?? playlistMetadataPrompts.ImagePrompt;
        if (string.IsNullOrWhiteSpace(prompt))
        {
            global::System.Console.WriteLine("Image prompt is empty. Skipping.");
            return false;
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
            return false;
        }

        await PersistTrackImagesAsync(playlist, track, position, prompt, model, aspectRatio, result);

        global::System.Console.WriteLine(
            $"image_generation complete playlist={playlistId} position={position} files={result.SavedFiles.Count}");
        return true;
    }

    private async Task<bool> GenerateMusicForPositionAsync(Guid playlistId, int position)
    {
        var workingDirectory = Environment.GetEnvironmentVariable("YT_PRODUCER_PLAYLIST_WORKING_DIRECTORY");
        if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            global::System.Console.WriteLine("YT_PRODUCER_PLAYLIST_WORKING_DIRECTORY is not configured.");
            return false;
        }

        var mcpWorkingDirectory = Environment.GetEnvironmentVariable("YT_PRODUCER_MCP_KIEAI_WORKING_DIRECTORY");
        if (string.IsNullOrWhiteSpace(mcpWorkingDirectory))
        {
            global::System.Console.WriteLine("YT_PRODUCER_MCP_KIEAI_WORKING_DIRECTORY is not configured.");
            return false;
        }

        var mcpProject = Environment.GetEnvironmentVariable("YT_PRODUCER_MCP_KIEAI_PROJECT")
            ?? "OnlineTeamTools.MCP.KieAi/OnlineTeamTools.MCP.KieAi.csproj";

        var playlist = await _context.Playlists
            .AsNoTracking()
            .Include(p => p.Tracks)
            .FirstOrDefaultAsync(p => p.Id == playlistId);
        if (playlist == null)
        {
            global::System.Console.WriteLine("Playlist not found.");
            return false;
        }

        var track = playlist.Tracks.FirstOrDefault(t => t.PlaylistPosition == position);
        if (track == null)
        {
            global::System.Console.WriteLine("Track not found for position.");
            return false;
        }

        var prompt = TryGetMetadataString(track.Metadata, "musicGenerationPrompt")
            ?? TryGetMetadataString(playlist.Metadata, "musicGenerationPrompt");
        if (string.IsNullOrWhiteSpace(prompt))
        {
            global::System.Console.WriteLine("musicGenerationPrompt is empty. Skipping.");
            return false;
        }

        var lyrics = TryGetMetadataString(track.Metadata, "lyrics") ?? string.Empty;
        var title = track.YouTubeTitle ?? track.Title;
        if (string.IsNullOrWhiteSpace(title))
        {
            title = $"track-{position}";
        }

        var model = Environment.GetEnvironmentVariable("YT_PRODUCER_KIEAI_SUNO_MODEL") ?? "V5";
        var vocalGender = Environment.GetEnvironmentVariable("YT_PRODUCER_KIEAI_SUNO_VOCAL_GENDER") ?? "m";
        var personaModel = Environment.GetEnvironmentVariable("YT_PRODUCER_KIEAI_SUNO_PERSONA_MODEL") ?? "style_persona";
        var callbackUrl = Environment.GetEnvironmentVariable("YT_PRODUCER_KIEAI_SUNO_CALLBACK_URL") ?? "playground";
        var customMode = !string.Equals(Environment.GetEnvironmentVariable("YT_PRODUCER_KIEAI_SUNO_CUSTOM_MODE"), "false", StringComparison.OrdinalIgnoreCase);
        var instrumental = !string.Equals(Environment.GetEnvironmentVariable("YT_PRODUCER_KIEAI_SUNO_INSTRUMENTAL"), "false", StringComparison.OrdinalIgnoreCase);
        var audioWeightText = Environment.GetEnvironmentVariable("YT_PRODUCER_KIEAI_SUNO_AUDIO_WEIGHT");
        var audioWeight = double.TryParse(audioWeightText, out var parsedAudioWeight) ? parsedAudioWeight : 0.65;

        var playlistFolderPath = Path.Combine(workingDirectory, GetPlaylistFolderName(playlist.Id));
        Directory.CreateDirectory(playlistFolderPath);

        global::System.Console.WriteLine($"music_generation start playlist={playlistId} position={position}");
        var result = await ExecuteMusicGenerationAsync(
            mcpWorkingDirectory,
            mcpProject,
            playlistFolderPath,
            title.Trim(),
            prompt.Trim(),
            lyrics.Trim(),
            vocalGender.Trim(),
            audioWeight,
            personaModel.Trim(),
            model.Trim(),
            instrumental,
            customMode,
            callbackUrl.Trim());

        if (!result.Success)
        {
            global::System.Console.WriteLine($"music_generation failed playlist={playlistId} position={position} error={result.ErrorMessage}");
            return false;
        }

        var targetFiles = ResolveTargetMusicPaths(playlistFolderPath, position, result.SavedFiles.Count);
        for (var i = 0; i < result.SavedFiles.Count; i++)
        {
            var saved = result.SavedFiles[i];
            var targetFilePath = targetFiles[i];

            try
            {
                var isSameAudioPath = string.Equals(saved.FilePath, targetFilePath, StringComparison.OrdinalIgnoreCase);
                if (File.Exists(targetFilePath) && !isSameAudioPath)
                {
                    File.Delete(targetFilePath);
                }

                if (!isSameAudioPath)
                {
                    File.Move(saved.FilePath, targetFilePath, true);
                }

                var sourceJsonPath = Path.ChangeExtension(saved.FilePath, ".json");
                var targetJsonPath = Path.ChangeExtension(targetFilePath, ".json");
                var isSameJsonPath = string.Equals(sourceJsonPath, targetJsonPath, StringComparison.OrdinalIgnoreCase);
                if (File.Exists(sourceJsonPath))
                {
                    if (File.Exists(targetJsonPath) && !isSameJsonPath)
                    {
                        File.Delete(targetJsonPath);
                    }

                    if (!isSameJsonPath)
                    {
                        File.Move(sourceJsonPath, targetJsonPath, true);
                    }
                }
            }
            catch (Exception ex)
            {
                global::System.Console.WriteLine($"music_generation failed playlist={playlistId} position={position} error=file rename failed: {ex.Message}");
                return false;
            }
        }

        global::System.Console.WriteLine(
            $"music_generation complete playlist={playlistId} position={position} files={result.SavedFiles.Count}");
        return true;
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

    private async Task<bool> UploadYoutubeVideoWithThumbnailForPositionAsync(Guid playlistId, int position, DateTimeOffset? publishAtOverride = null)
    {
        var videoUploaded = await UploadYoutubeVideoAsync(playlistId, position, publishAtOverride);
        if (!videoUploaded)
        {
            return false;
        }

        return await UploadYoutubeThumbnailAsync(playlistId, position);
    }

    private async Task<bool> UploadYoutubeVideoAsync(Guid playlistId, int position, DateTimeOffset? publishAtOverride = null)
    {
        var workingDirectory = Environment.GetEnvironmentVariable("YT_PRODUCER_PLAYLIST_WORKING_DIRECTORY");
        if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            global::System.Console.WriteLine("YT_PRODUCER_PLAYLIST_WORKING_DIRECTORY is not configured.");
            return false;
        }

        var mcpWorkingDirectory = Environment.GetEnvironmentVariable("YT_PRODUCER_MCP_YOUTUBE_WORKING_DIRECTORY");
        if (string.IsNullOrWhiteSpace(mcpWorkingDirectory))
        {
            global::System.Console.WriteLine("YT_PRODUCER_MCP_YOUTUBE_WORKING_DIRECTORY is not configured.");
            return false;
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
            return false;
        }

        var track = playlist.Tracks.FirstOrDefault(t => t.PlaylistPosition == position);
        if (track == null)
        {
            global::System.Console.WriteLine("Track not found for position.");
            return false;
        }

        var existing = await _context.Set<TrackOnYoutube>()
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.TrackId == track.Id);
        if (existing != null)
        {
            global::System.Console.WriteLine($"Track already uploaded: {existing.VideoId}");
            return true;
        }

        var playlistFolderPath = Path.Combine(workingDirectory, GetPlaylistFolderName(playlistId));
        var videoPath = ResolveVideoPathForPosition(playlistFolderPath, position);
        if (videoPath == null)
        {
            global::System.Console.WriteLine($"Video file not found for position {position}.");
            return false;
        }

        var durationSeconds = await ProbeMediaDurationSecondsAsync(videoPath)
            ?? ParseTrackDurationSeconds(track.Duration);
        var uploadMetadata = _youtubeSeoService.BuildTrackUploadMetadata(
            track,
            playlist,
            durationSeconds,
            playlist.YoutubePlaylistId);
        var publishState = await GetOrCreateYoutubeLastPublishedDateAsync();
        var publishAt = publishAtOverride ?? GetNextYoutubePublishSlotUtc(publishState.LastPublishedDate);
        var scheduledPrivacy = "private";

        var result = await ExecuteYoutubeUploadVideoAsync(
            mcpWorkingDirectory,
            mcpProject,
            allowedRoot,
            videoPath,
            uploadMetadata,
            scheduledPrivacy,
            publishAt);

        if (!result.Success || string.IsNullOrWhiteSpace(result.VideoId))
        {
            global::System.Console.WriteLine($"youtube.upload_video failed: {result.ErrorMessage}");
            return false;
        }

        var record = new TrackOnYoutube
        {
            Id = Guid.NewGuid(),
            TrackId = track.Id,
            PlaylistId = playlistId,
            PlaylistPosition = position,
            VideoId = result.VideoId,
            Url = result.Url,
            Title = uploadMetadata.Title,
            Description = uploadMetadata.Description,
            Privacy = scheduledPrivacy,
            FilePath = videoPath,
            Status = "uploaded",
            Metadata = JsonSerializer.Serialize(new
            {
                tags = uploadMetadata.Tags,
                hashtags = uploadMetadata.Hashtags,
                chapters = uploadMetadata.Chapters,
                categoryId = uploadMetadata.CategoryId,
                defaultLanguage = uploadMetadata.DefaultLanguage,
                defaultAudioLanguage = uploadMetadata.DefaultAudioLanguage,
                madeForKids = uploadMetadata.MadeForKids,
                scheduledPublishAtUtc = publishAt
            }),
            ScheduledPublishAtUtc = publishAt,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        if (publishAt > publishState.LastPublishedDate)
        {
            publishState.LastPublishedDate = publishAt;
            publishState.VideoId = result.VideoId;
        }
        _context.Add(record);
        await _context.SaveChangesAsync();

        var youtubePlaylistId = await TryResolveYoutubePlaylistIdAsync(playlist);
        if (!string.IsNullOrWhiteSpace(youtubePlaylistId))
        {
            var addResult = await ExecuteYoutubeAddVideosToPlaylistAsync(
                mcpWorkingDirectory,
                mcpProject,
                youtubePlaylistId,
                [result.VideoId]);

            if (addResult.Success)
            {
                record.Metadata = JsonSerializer.Serialize(new
                {
                    tags = uploadMetadata.Tags,
                    hashtags = uploadMetadata.Hashtags,
                    chapters = uploadMetadata.Chapters,
                    categoryId = uploadMetadata.CategoryId,
                    defaultLanguage = uploadMetadata.DefaultLanguage,
                    defaultAudioLanguage = uploadMetadata.DefaultAudioLanguage,
                    madeForKids = uploadMetadata.MadeForKids,
                    scheduledPublishAtUtc = publishAt,
                    youtubePlaylistId,
                    addedToPlaylist = true
                });
                _context.TrackOnYoutube.Update(record);
                await _context.SaveChangesAsync();
            }
            else
            {
                global::System.Console.WriteLine($"youtube.add_videos_to_playlist failed after upload: {addResult.ErrorMessage}");
            }
        }

        global::System.Console.WriteLine($"youtube upload complete: {result.VideoId} scheduled_at_utc={publishAt:yyyy-MM-ddTHH:mm:ssZ}");
        return true;
    }

    private async Task<bool> UploadYoutubeThumbnailAsync(Guid playlistId, int position)
    {
        var workingDirectory = Environment.GetEnvironmentVariable("YT_PRODUCER_PLAYLIST_WORKING_DIRECTORY");
        if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            global::System.Console.WriteLine("YT_PRODUCER_PLAYLIST_WORKING_DIRECTORY is not configured.");
            return false;
        }

        var mcpWorkingDirectory = Environment.GetEnvironmentVariable("YT_PRODUCER_MCP_YOUTUBE_WORKING_DIRECTORY");
        if (string.IsNullOrWhiteSpace(mcpWorkingDirectory))
        {
            global::System.Console.WriteLine("YT_PRODUCER_MCP_YOUTUBE_WORKING_DIRECTORY is not configured.");
            return false;
        }

        var mcpProject = Environment.GetEnvironmentVariable("YT_PRODUCER_MCP_YOUTUBE_PROJECT")
            ?? "OnlineTeamTools.MCP.YouTube/OnlineTeamTools.MCP.YouTube.csproj";

        var allowedRoot = Environment.GetEnvironmentVariable("YT_PRODUCER_YOUTUBE_ALLOWED_ROOT")
            ?? Environment.GetEnvironmentVariable("YT_PRODUCER_PLAYLIST_WORKING_DIRECTORY");

        var playlistFolderPath = Path.Combine(workingDirectory, GetPlaylistFolderName(playlistId));
        if (!Directory.Exists(playlistFolderPath))
        {
            global::System.Console.WriteLine($"Playlist folder not found: {playlistFolderPath}");
            return false;
        }

        var imagePath = ResolveYoutubeThumbnailPathForPosition(playlistFolderPath, position)
            ?? ResolveImagePathForPosition(playlistFolderPath, position);
        if (imagePath == null)
        {
            global::System.Console.WriteLine($"Image file not found for position {position}.");
            return false;
        }

        var fileInfo = new FileInfo(imagePath);
        if (fileInfo.Length > 2L * 1024 * 1024 * 1024)
        {
            global::System.Console.WriteLine("Thumbnail file exceeds 2GB limit.");
            return false;
        }

        var track = await _context.Tracks
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.PlaylistId == playlistId && t.PlaylistPosition == position);
        if (track == null)
        {
            global::System.Console.WriteLine("Track not found for position.");
            return false;
        }

        var youtubeRecord = await _context.Set<TrackOnYoutube>()
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.TrackId == track.Id);
        if (youtubeRecord == null || string.IsNullOrWhiteSpace(youtubeRecord.VideoId))
        {
            global::System.Console.WriteLine("No YouTube video record found for this track. Upload video first.");
            return false;
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
            return false;
        }

        global::System.Console.WriteLine($"youtube thumbnail uploaded: {youtubeRecord.VideoId}");
        return true;
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
            .Where(x => TryGetMetadataBool(x.Metadata, "addedToPlaylist") != true)
            .Select(x => x.VideoId?.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .Cast<string>()
            .ToList();

        if (videoIds.Count == 0)
        {
            global::System.Console.WriteLine("No pending uploaded videos found in track_on_youtube for this playlist.");
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

    private async Task<bool> CreateYoutubeVideoThumbnailAsync(Guid playlistId, int position)
    {
        var workingDirectory = Environment.GetEnvironmentVariable("YT_PRODUCER_PLAYLIST_WORKING_DIRECTORY");
        if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            global::System.Console.WriteLine("YT_PRODUCER_PLAYLIST_WORKING_DIRECTORY is not configured.");
            return false;
        }

        var mcpMediaWorkingDirectory = Environment.GetEnvironmentVariable("YT_PRODUCER_MCP_MEDIA_WORKING_DIRECTORY");
        if (string.IsNullOrWhiteSpace(mcpMediaWorkingDirectory))
        {
            global::System.Console.WriteLine("YT_PRODUCER_MCP_MEDIA_WORKING_DIRECTORY is not configured.");
            return false;
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
            return false;
        }

        var track = await _context.Tracks
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.PlaylistId == playlistId && t.PlaylistPosition == position);
        if (track == null)
        {
            global::System.Console.WriteLine("Track not found for position.");
            return false;
        }

        var playlistFolderPath = Path.Combine(workingDirectory, GetPlaylistFolderName(playlistId));
        if (!Directory.Exists(playlistFolderPath))
        {
            global::System.Console.WriteLine($"Playlist folder not found: {playlistFolderPath}");
            return false;
        }

        var imagePath = ResolveImagePathForPosition(playlistFolderPath, position);
        if (imagePath == null)
        {
            global::System.Console.WriteLine($"Image file not found for position {position}.");
            return false;
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
                return false;
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
            return false;
        }

        global::System.Console.WriteLine(
            $"track thumbnail generated playlist={playlistId} position={position} file={result.OutputPath ?? outputPath}");
        return true;
    }

    private async Task<bool> CreateYoutubeVideoThumbnailV2Async(Guid playlistId, int position)
    {
        var setup = await PrepareYoutubeThumbnailGenerationAsync(playlistId, position);
        if (setup == null)
        {
            return false;
        }

        var request = new CreateYoutubeThumbnailRequest
        {
            ImagePath = setup.ImagePath,
            LogoPath = setup.LogoPath,
            Headline = setup.Headline,
            Subheadline = setup.Subheadline,
            OutputPath = setup.OutputPath,
            Style = new CreateYoutubeThumbnailStyleRequest
            {
                HeadlineFont = setup.HeadlineFont,
                SubheadlineFont = setup.SubheadlineFont,
                HeadlineColor = setup.HeadlineColor,
                SubheadlineColor = setup.SubheadlineColor,
                Shadow = true,
                Stroke = true
            }
        };

        try
        {
            var service = new YoutubeThumbnailService();
            var result = await service.CreateAsync(request, CancellationToken.None);
            if (!result.Ok)
            {
                global::System.Console.WriteLine(
                    $"track thumbnail generation failed playlist={playlistId} position={position} error=local media service returned ok=false");
                return false;
            }

            global::System.Console.WriteLine(
                $"track thumbnail generated playlist={playlistId} position={position} file={result.OutputPath ?? setup.OutputPath}");
            return true;
        }
        catch (Exception ex)
        {
            global::System.Console.WriteLine(
                $"track thumbnail generation failed playlist={playlistId} position={position} error={ex.Message}");
            return false;
        }
    }

    private async Task<YoutubeThumbnailGenerationSetup?> PrepareYoutubeThumbnailGenerationAsync(Guid playlistId, int position)
    {
        var workingDirectory = Environment.GetEnvironmentVariable("YT_PRODUCER_PLAYLIST_WORKING_DIRECTORY");
        if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            global::System.Console.WriteLine("YT_PRODUCER_PLAYLIST_WORKING_DIRECTORY is not configured.");
            return null;
        }

        var playlist = await _context.Playlists
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == playlistId);
        if (playlist == null)
        {
            global::System.Console.WriteLine("Playlist not found.");
            return null;
        }

        var track = await _context.Tracks
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.PlaylistId == playlistId && t.PlaylistPosition == position);
        if (track == null)
        {
            global::System.Console.WriteLine("Track not found for position.");
            return null;
        }

        var playlistFolderPath = Path.Combine(workingDirectory, GetPlaylistFolderName(playlistId));
        if (!Directory.Exists(playlistFolderPath))
        {
            global::System.Console.WriteLine($"Playlist folder not found: {playlistFolderPath}");
            return null;
        }

        var imagePath = ResolveImagePathForPosition(playlistFolderPath, position);
        if (imagePath == null)
        {
            global::System.Console.WriteLine($"Image file not found for position {position}.");
            return null;
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
                return null;
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

        return new YoutubeThumbnailGenerationSetup(
            ImagePath: imagePath,
            OutputPath: outputPath,
            LogoPath: logoPath,
            Headline: headline,
            Subheadline: subheadline,
            HeadlineFont: headlineFont,
            SubheadlineFont: subheadlineFont,
            HeadlineColor: headlineColor,
            SubheadlineColor: subheadlineColor);
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
            ?? playlist.Theme
            ?? TryGetMetadataString(playlist.Metadata, "playlistCategory")
            ?? string.Empty;

        var musicPrompt = TryGetMetadataString(track.Metadata, "musicGenerationPrompt")
            ?? TryGetMetadataString(playlist.Metadata, "musicGenerationPrompt");
        var subgenre = TryExtractFieldFromPrompt(musicPrompt, "Subgenre")
            ?? TryExtractFieldFromPrompt(musicPrompt, "Genre");

        var scenarioToken = NormalizeHeadline(scenario);
        if (!string.IsNullOrWhiteSpace(subgenre))
        {
            return string.IsNullOrWhiteSpace(scenarioToken)
                ? NormalizeHeadline(subgenre)
                : $"{NormalizeHeadline(subgenre)} {scenarioToken}";
        }

        return scenarioToken;
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

    private static string BuildMusicGenerationRequestJson(
        string title,
        string style,
        string prompt,
        string vocalGender,
        double audioWeight,
        string personaModel,
        string outputFilesPath,
        string model,
        bool instrumental,
        bool customMode,
        string callbackUrl)
    {
        var payload = new
        {
            jsonrpc = "2.0",
            id = 16,
            method = "tools/call",
            @params = new
            {
                name = "suno_generate_music",
                arguments = new
                {
                    title,
                    style,
                    prompt,
                    vocalGender,
                    audioWeight,
                    personaModel,
                    outputFilesPath,
                    model,
                    instrumental,
                    customMode,
                    callBackUrl = callbackUrl
                }
            }
        };

        return JsonSerializer.Serialize(payload);
    }

    private static string BuildChatCompletionRequestJson(
        string model,
        string systemPrompt,
        string userPrompt)
    {
        var payload = new
        {
            jsonrpc = "2.0",
            id = 18,
            method = "tools/call",
            @params = new
            {
                name = "chat_completion",
                arguments = new
                {
                    model,
                    stream = false,
                    include_thoughts = false,
                    reasoning_effort = "high",
                    messages = new object[]
                    {
                        new
                        {
                            role = "system",
                            content = new object[]
                            {
                                new
                                {
                                    type = "text",
                                    text = systemPrompt
                                }
                            }
                        },
                        new
                        {
                            role = "user",
                            content = new object[]
                            {
                                new
                                {
                                    type = "text",
                                    text = userPrompt
                                }
                            }
                        }
                    }
                }
            }
        };

        return JsonSerializer.Serialize(payload);
    }

    private async Task<MusicGenerationExecutionResult> ExecuteMusicGenerationAsync(
        string mcpWorkingDirectory,
        string mcpProject,
        string outputFilesPath,
        string title,
        string style,
        string prompt,
        string vocalGender,
        double audioWeight,
        string personaModel,
        string model,
        bool instrumental,
        bool customMode,
        string callbackUrl)
    {
        try
        {
            var requestJson = BuildMusicGenerationRequestJson(
                title,
                style,
                prompt,
                vocalGender,
                audioWeight,
                personaModel,
                outputFilesPath,
                model,
                instrumental,
                customMode,
                callbackUrl);

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
                return MusicGenerationExecutionResult.Fail(error);
            }

            var jsonLine = TryExtractJsonRpcLine(stdOut);
            if (string.IsNullOrWhiteSpace(jsonLine))
            {
                return MusicGenerationExecutionResult.Fail("MCP response did not contain JSON-RPC payload.");
            }

            using var responseDoc = JsonDocument.Parse(jsonLine);
            var root = responseDoc.RootElement;
            if (root.TryGetProperty("error", out var errorNode))
            {
                return MusicGenerationExecutionResult.Fail(errorNode.GetRawText());
            }

            JsonElement resultNode = default;
            if (root.TryGetProperty("result", out var directResult))
            {
                resultNode = directResult;
                if (directResult.ValueKind == JsonValueKind.Object &&
                    directResult.TryGetProperty("data", out var dataResult) &&
                    dataResult.ValueKind == JsonValueKind.Object)
                {
                    resultNode = dataResult;
                }
            }

            var savedFiles = new List<SavedAudioArtifact>();
            if (resultNode.ValueKind == JsonValueKind.Object &&
                resultNode.TryGetProperty("downloadedFiles", out var downloadedFilesNode) &&
                downloadedFilesNode.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in downloadedFilesNode.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    var filePath = TryGetStringProperty(item, "filePath");
                    if (string.IsNullOrWhiteSpace(filePath))
                    {
                        continue;
                    }

                    savedFiles.Add(new SavedAudioArtifact(
                        filePath,
                        Path.GetFileName(filePath),
                        TryGetStringProperty(item, "sourceUrl")));
                }
            }

            if (savedFiles.Count == 0)
            {
                return MusicGenerationExecutionResult.Fail("No downloaded audio files returned by MCP.");
            }

            return MusicGenerationExecutionResult.Ok(savedFiles);
        }
        catch (Exception ex)
        {
            return MusicGenerationExecutionResult.Fail(ex.Message);
        }
    }

    private async Task<ChatCompletionExecutionResult> ExecutePromptGenerationAsync(
        string provider,
        string model,
        string systemPrompt,
        string userPrompt)
    {
        var normalizedProvider = provider?.Trim().ToLowerInvariant();
        if (normalizedProvider == "kie_ai" || normalizedProvider == "kieai" || normalizedProvider == "kie")
        {
            try
            {
                var client = _reasoningClientFactory.GetClient(ReasoningProvider.KieAi);
                var response = await client.CompleteAsync(
                    new ReasoningRequest(
                        model,
                        systemPrompt,
                        userPrompt),
                    CancellationToken.None);

                return ChatCompletionExecutionResult.Ok(
                    response.Text,
                    response.Model,
                    response.FinishReason,
                    response.RawResponseJson,
                    response.Usage is null ? null : JsonSerializer.Serialize(response.Usage));
            }
            catch (ReasoningClientException ex)
            {
                var message = string.IsNullOrWhiteSpace(ex.ResponseBody)
                    ? ex.Message
                    : $"{ex.Message} Response: {ex.ResponseBody}";
                return ChatCompletionExecutionResult.Fail(message);
            }
            catch (Exception ex)
            {
                return ChatCompletionExecutionResult.Fail(ex.Message);
            }
        }

        try
        {
            var mcpWorkingDirectory = Environment.GetEnvironmentVariable("YT_PRODUCER_MCP_KIEAI_WORKING_DIRECTORY");
            if (string.IsNullOrWhiteSpace(mcpWorkingDirectory))
            {
                throw new InvalidOperationException("YT_PRODUCER_MCP_KIEAI_WORKING_DIRECTORY is not configured.");
            }

            var mcpProject = Environment.GetEnvironmentVariable("YT_PRODUCER_MCP_KIEAI_PROJECT")
                ?? "OnlineTeamTools.MCP.KieAi/OnlineTeamTools.MCP.KieAi.csproj";

            var requestJson = BuildChatCompletionRequestJson(model, systemPrompt, userPrompt);

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
                return ChatCompletionExecutionResult.Fail(error);
            }

            var jsonLine = TryExtractJsonRpcLine(stdOut);
            if (string.IsNullOrWhiteSpace(jsonLine))
            {
                return ChatCompletionExecutionResult.Fail("MCP response did not contain JSON-RPC payload.");
            }

            using var responseDoc = JsonDocument.Parse(jsonLine);
            var root = responseDoc.RootElement;
            if (root.TryGetProperty("error", out var errorNode))
            {
                return ChatCompletionExecutionResult.Fail(errorNode.GetRawText());
            }

            JsonElement resultNode = default;
            if (root.TryGetProperty("result", out var directResult))
            {
                resultNode = directResult;
                if (directResult.ValueKind == JsonValueKind.Object &&
                    directResult.TryGetProperty("data", out var dataResult) &&
                    dataResult.ValueKind == JsonValueKind.Object)
                {
                    resultNode = dataResult;
                }
            }

            if (resultNode.ValueKind != JsonValueKind.Object)
            {
                return ChatCompletionExecutionResult.Fail("Kie chat_completion result payload is missing.");
            }

            var text = TryGetStringProperty(resultNode, "text");
            if (string.IsNullOrWhiteSpace(text))
            {
                return ChatCompletionExecutionResult.Fail("Kie chat_completion did not return text.");
            }

            return ChatCompletionExecutionResult.Ok(text, model, null, resultNode.GetRawText(), null);
        }
        catch (Exception ex)
        {
            return ChatCompletionExecutionResult.Fail(ex.Message);
        }
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
        YoutubeUploadMetadata uploadMetadata,
        string privacy,
        DateTimeOffset? publishAt)
    {
        var arguments = new Dictionary<string, object?>
        {
            ["file_path"] = filePath,
            ["title"] = uploadMetadata.Title,
            ["description"] = uploadMetadata.Description,
            ["privacy"] = privacy,
            ["publish_at"] = publishAt?.ToUniversalTime().ToString("O")
        };

        if (uploadMetadata.Tags.Count > 0)
        {
            arguments["tags"] = uploadMetadata.Tags;
        }

        if (uploadMetadata.CategoryId.HasValue)
        {
            arguments["category_id"] = uploadMetadata.CategoryId.Value.ToString(CultureInfo.InvariantCulture);
        }

        arguments["made_for_kids"] = uploadMetadata.MadeForKids;
        var includeLanguageFields = ParseNullableBool(
            Environment.GetEnvironmentVariable("YT_PRODUCER_YOUTUBE_INCLUDE_LANGUAGE_FIELDS")) == true;
        if (includeLanguageFields)
        {
            arguments["default_language"] = uploadMetadata.DefaultLanguage;
            arguments["default_audio_language"] = uploadMetadata.DefaultAudioLanguage;
        }

        var payload = new
        {
            jsonrpc = "2.0",
            id = 11,
            method = "tools/call",
            @params = new
            {
                name = "youtube.upload_video",
                arguments
            }
        };

        return JsonSerializer.Serialize(payload);
    }

    private async Task<YoutubeUploadVideoResult> ExecuteYoutubeUploadVideoAsync(
        string mcpWorkingDirectory,
        string mcpProject,
        string? allowedRoot,
        string filePath,
        YoutubeUploadMetadata uploadMetadata,
        string privacy,
        DateTimeOffset? publishAt)
    {
        try
        {
            var requestJson = BuildYoutubeUploadVideoRequestJson(filePath, uploadMetadata, privacy, publishAt);
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

    private static bool? TryGetBoolProperty(JsonElement root, string propertyName)
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

            if (property.Value.ValueKind == JsonValueKind.True || property.Value.ValueKind == JsonValueKind.False)
            {
                return property.Value.GetBoolean();
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

    private async Task<string?> TryResolveYoutubePlaylistIdAsync(Playlist playlist)
    {
        if (!string.IsNullOrWhiteSpace(playlist.YoutubePlaylistId))
        {
            return playlist.YoutubePlaylistId;
        }

        var youtubePlaylistRecord = await _context.YoutubePlaylists
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.PlaylistId == playlist.Id);

        return youtubePlaylistRecord?.YoutubePlaylistId;
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

    private static string BuildYoutubeAddCommentRequestJson(string videoId, string text)
    {
        var payload = new
        {
            jsonrpc = "2.0",
            id = 15,
            method = "tools/call",
            @params = new
            {
                name = "youtube.add_comment",
                arguments = new
                {
                    video_id = videoId,
                    text
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

    private async Task<YoutubeAddCommentResult> ExecuteYoutubeAddCommentAsync(
        string mcpWorkingDirectory,
        string mcpProject,
        string videoId,
        string text)
    {
        try
        {
            var requestJson = BuildYoutubeAddCommentRequestJson(videoId, text);
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
                return YoutubeAddCommentResult.Fail(error);
            }

            var jsonLine = TryExtractJsonRpcLine(stdOut);
            if (string.IsNullOrWhiteSpace(jsonLine))
            {
                return YoutubeAddCommentResult.Fail("MCP response did not contain JSON-RPC payload.");
            }

            using var responseDoc = JsonDocument.Parse(jsonLine);
            var root = responseDoc.RootElement;

            if (root.TryGetProperty("error", out var errorNode))
            {
                return YoutubeAddCommentResult.Fail(errorNode.GetRawText());
            }

            if (root.TryGetProperty("result", out var resultNode))
            {
                if (resultNode.TryGetProperty("data", out var dataNode))
                {
                    resultNode = dataNode;
                }

                var commentId = TryGetStringProperty(resultNode, "comment_id");
                if (!string.IsNullOrWhiteSpace(commentId))
                {
                    return YoutubeAddCommentResult.Ok(commentId);
                }
            }

            return YoutubeAddCommentResult.Fail("comment_id not returned from MCP.");
        }
        catch (Exception ex)
        {
            return YoutubeAddCommentResult.Fail(ex.Message);
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

        var audioByPosition = files
            .Where(path => AudioExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            .Select(path => new
            {
                Path = path,
                BaseName = Path.GetFileNameWithoutExtension(path),
                SortKey = ParseMediaSortKey(Path.GetFileNameWithoutExtension(path))
            })
            .Where(x => x.SortKey.Position != int.MaxValue)
            .GroupBy(x => x.SortKey.Position)
            .ToDictionary(
                g => g.Key,
                g => g
                    .OrderBy(x => x.SortKey.Variant)
                    .ThenBy(x => x.BaseName, StringComparer.OrdinalIgnoreCase)
                    .Select(x => x.Path)
                    .First());

        var imageByPosition = files
            .Where(path => ImageExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            .Select(path => new
            {
                Path = path,
                BaseName = Path.GetFileNameWithoutExtension(path),
                SortKey = ParseMediaSortKey(Path.GetFileNameWithoutExtension(path))
            })
            .Where(x => x.SortKey.Position != int.MaxValue)
            .GroupBy(x => x.SortKey.Position)
            .ToDictionary(
                g => g.Key,
                g => g
                    .OrderBy(x => x.SortKey.Variant)
                    .ThenBy(x => x.BaseName, StringComparer.OrdinalIgnoreCase)
                    .Select(x => x.Path)
                    .First());

        var candidates = new List<RenderCandidate>();
        foreach (var position in audioByPosition.Keys.Intersect(imageByPosition.Keys).OrderBy(x => x))
        {
            var key = position.ToString();
            candidates.Add(new RenderCandidate(
                Key: key,
                ImagePath: imageByPosition[position],
                AudioPath: audioByPosition[position],
                LocalVideoPath: Path.Combine(playlistFolderPath, $"{key}.mp4")));
        }

        return candidates;
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
            LastPublishedDate = new DateTimeOffset(2026, 3, 8, 7, 30, 0, TimeSpan.Zero),
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

        foreach (var slot in YoutubePublishSlotsUtc.OrderBy(s => s.Hour).ThenBy(s => s.Minute))
        {
            var candidate = new DateTimeOffset(date.Year, date.Month, date.Day, slot.Hour, slot.Minute, 0, TimeSpan.Zero);
            if (candidate > utc)
            {
                return candidate;
            }
        }

        var nextDay = date.AddDays(1);
        var firstSlot = YoutubePublishSlotsUtc
            .OrderBy(s => s.Hour)
            .ThenBy(s => s.Minute)
            .First();
        return new DateTimeOffset(nextDay.Year, nextDay.Month, nextDay.Day, firstSlot.Hour, firstSlot.Minute, 0, TimeSpan.Zero);
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

    private static string NormalizeReturnedJson(string rawText)
    {
        var trimmed = rawText.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            return trimmed;
        }

        var normalized = trimmed.Trim('`').Trim();
        if (normalized.StartsWith("json", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[4..].Trim();
        }

        return normalized;
    }

    private static bool TryFormatJson(string rawText, out string? formattedJson, out string? validationErrors)
    {
        try
        {
            using var document = JsonDocument.Parse(rawText);
            formattedJson = JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            validationErrors = null;
            return true;
        }
        catch (JsonException ex)
        {
            formattedJson = null;
            validationErrors = ex.Message;
            return false;
        }
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

    private static string? ResolveAlbumReleaseSourceThumbnailPath(string playlistFolderPath)
    {
        var preferred = ImageExtensions
            .Select(ext => Path.Combine(playlistFolderPath, $"album_release_thumbnail{ext}"))
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
                return string.Equals(baseName, "album_release_thumbnail", StringComparison.OrdinalIgnoreCase);
            })
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return matches.FirstOrDefault();
    }

    private static List<string> ResolveTargetMusicPaths(string playlistFolderPath, int position, int count)
    {
        var targets = new List<string>();
        for (var i = 1; i <= count; i++)
        {
            var fileName = i == 1 ? $"{position}.mp3" : $"{position}_{i}.mp3";
            targets.Add(Path.Combine(playlistFolderPath, fileName));
        }

        return targets;
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

    private static bool? TryGetMetadataBool(string? metadata, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(metadata))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(metadata);
            return TryGetBoolProperty(document.RootElement, propertyName);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool? ParseNullableBool(string? value)
    {
        return bool.TryParse(value?.Trim(), out var parsed) ? parsed : null;
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

        foreach (var position in missingPositions)
        {
            var metadataPrompt = trackPromptsByPosition.TryGetValue(position, out var trackData)
                ? trackData.ImagePrompt
                : null;
            var prompt = metadataPrompt
                ?? playlistLevelImagePrompt
                ?? $"Square cover art for track {position} in playlist \"{safeTitle}\". Theme: {safeTheme}. Bold composition, clean typography space, professional YouTube quality.";
            lines.Add($"{position}: {prompt}");
            lines.Add(string.Empty);
        }

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

        foreach (var position in missingPositions)
        {
            var metadataPrompt = trackPromptsByPosition.TryGetValue(position, out var trackData)
                ? trackData.SunoPrompt
                : null;
            var prompt = metadataPrompt
                ?? playlistLevelSunoPrompt
                ?? $"Create track {position} for playlist \"{safeTitle}\". Theme: {safeTheme}. Instrumental, 3-4 minutes, strong hook, modern production, no vocals.";
            lines.Add($"{position}: {prompt}");
            lines.Add(string.Empty);
        }

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

    private sealed record SavedAudioArtifact(string FilePath, string FileName, string? SourceUrl);

    private sealed record MusicGenerationExecutionResult(bool Success, IReadOnlyList<SavedAudioArtifact> SavedFiles, string? ErrorMessage)
    {
        public static MusicGenerationExecutionResult Ok(IReadOnlyList<SavedAudioArtifact> savedFiles) => new(true, savedFiles, null);
        public static MusicGenerationExecutionResult Fail(string errorMessage) => new(false, Array.Empty<SavedAudioArtifact>(), errorMessage);
    }

    private sealed record ChatCompletionExecutionResult(
        bool Success,
        string? RawText,
        string? ErrorMessage,
        string? Model,
        string? FinishReason,
        string? RawResponseJson,
        string? UsageJson)
    {
        public static ChatCompletionExecutionResult Ok(string rawText, string? model, string? finishReason, string? rawResponseJson, string? usageJson)
            => new(true, rawText, null, model, finishReason, rawResponseJson, usageJson);

        public static ChatCompletionExecutionResult Fail(string errorMessage)
            => new(false, null, errorMessage, null, null, null, null);
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

    private sealed record YoutubeAddCommentResult(bool Success, string? CommentId, string? ErrorMessage)
    {
        public static YoutubeAddCommentResult Ok(string commentId) => new(true, commentId, null);
        public static YoutubeAddCommentResult Fail(string errorMessage) => new(false, null, errorMessage);
    }

    private sealed record AlbumReleaseAssetsResult(string OutputVideoPath, string? ThumbnailPath, string TempRootPath);

    private sealed record LoopConcatResult(bool Success, string CommandLine, string? ErrorMessage);

    private sealed record PromptMetadata(string? ImagePrompt, string? SunoPrompt);

    private sealed record TrackPromptData(string? TrackTitle, string? ImagePrompt, string? SunoPrompt);

    private sealed record TestGenerateVideoProfile(
        string ImagePath,
        string AudioPath,
        string TempDir,
        string OutputDir,
        int Width,
        int Height,
        int Fps,
        int EqBands,
        string VideoBitrate,
        string AudioBitrate,
        int Seed,
        bool UseGpu,
        bool KeepTemp,
        bool UseRawPipe,
        string RendererVariant,
        string? OutputFileNameOverride,
        string? LogoPath);

    private sealed record YoutubeThumbnailGenerationSetup(
        string ImagePath,
        string OutputPath,
        string? LogoPath,
        string Headline,
        string Subheadline,
        string HeadlineFont,
        string SubheadlineFont,
        string? HeadlineColor,
        string? SubheadlineColor);

    private sealed record LocalMediaRenderProfile(
        string PublicName,
        string RendererVariant,
        int Width,
        int Height,
        int Fps,
        int EqBands,
        string VideoBitrate,
        string AudioBitrate,
        int Seed,
        bool UseGpu,
        bool UseRawPipe,
        string? LogoPath);

    private sealed class ProgressFileState
    {
        public string? CurrentPath { get; set; }
    }

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

    private async Task<int> GenerateYoutubeEngagementsForPlaylistAsync(Guid jobId, Guid playlistId)
    {
        const string engagementType = "pinned_comment_candidate";
        const string templateSlug = "youtube-pinned-comment-candidate";

        var playlist = await _context.Playlists
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == playlistId);
        if (playlist == null)
        {
            throw new InvalidOperationException($"Playlist not found: {playlistId}");
        }

        var youtubePlaylist = await _context.YoutubePlaylists
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.PlaylistId == playlistId);

        var channelId = !string.IsNullOrWhiteSpace(youtubePlaylist?.ChannelId)
            ? youtubePlaylist.ChannelId!.Trim()
            : (Environment.GetEnvironmentVariable("YT_PRODUCER_YOUTUBE_CHANNEL_ID")?.Trim() ?? "default");

        var template = await _context.PromptTemplates
            .FirstOrDefaultAsync(x => x.Slug == templateSlug && x.IsActive);
        if (template == null)
        {
            throw new InvalidOperationException($"Prompt template not found: {templateSlug}");
        }

        var uploadedTracks = await _context.TrackOnYoutube
            .AsNoTracking()
            .Where(x => x.PlaylistId == playlistId)
            .OrderBy(x => x.PlaylistPosition)
            .ToListAsync();

        if (uploadedTracks.Count == 0)
        {
            throw new InvalidOperationException("No uploaded track videos found for this playlist.");
        }

        var trackIds = uploadedTracks.Select(x => x.TrackId).Distinct().ToList();
        var tracks = await _context.Tracks
            .AsNoTracking()
            .Where(x => trackIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id);

        var generatedCount = 0;

        foreach (var uploadedTrack in uploadedTracks)
        {
            if (!tracks.TryGetValue(uploadedTrack.TrackId, out var track))
            {
                await AppendJobLogAsync(jobId, "Warning", $"Track metadata missing for uploaded video track_id={uploadedTrack.TrackId}");
                continue;
            }

            var inputJson = BuildYoutubeEngagementInputJson(track, playlist, uploadedTrack);
            var inputLabel = $"{track.Title} pinned comment candidate";
            var resolvedSystemPrompt = template.SystemPrompt?.Trim() ?? string.Empty;
            var resolvedUserPrompt = RenderPromptUserTemplate(template, inputLabel, inputJson);

            var generation = new PromptGeneration
            {
                Id = Guid.NewGuid(),
                TemplateId = template.Id,
                Purpose = template.Category,
                Provider = template.Provider,
                Status = PromptGenerationStatus.Running,
                Model = string.IsNullOrWhiteSpace(template.DefaultModel) ? "gemini-2.5-pro" : template.DefaultModel!.Trim(),
                InputLabel = inputLabel,
                InputJson = inputJson,
                ResolvedSystemPrompt = resolvedSystemPrompt,
                ResolvedUserPrompt = resolvedUserPrompt,
                TargetType = "track",
                TargetId = track.Id.ToString(),
                CreatedAtUtc = DateTimeOffset.UtcNow,
                StartedAtUtc = DateTimeOffset.UtcNow,
                TokenUsageJson = "{}",
                RunMetadataJson = "{}"
            };

            var engagement = await _context.YoutubeVideoEngagements
                .FirstOrDefaultAsync(x =>
                    x.PlaylistId == playlistId &&
                    x.TrackId == track.Id &&
                    x.YoutubeVideoId == uploadedTrack.VideoId &&
                    x.EngagementType == engagementType);

            if (engagement == null)
            {
                engagement = new YoutubeVideoEngagement
                {
                    Id = Guid.NewGuid(),
                    ChannelId = channelId,
                    YoutubeVideoId = uploadedTrack.VideoId,
                    TrackId = track.Id,
                    PlaylistId = playlistId,
                    EngagementType = engagementType,
                    Status = YoutubeVideoEngagementStatus.Draft,
                    MetadataJson = JsonSerializer.Serialize(new
                    {
                        trackTitle = track.Title,
                        playlistTitle = playlist.Title,
                        playlistPosition = track.PlaylistPosition,
                        youtubeTitle = uploadedTrack.Title ?? track.YouTubeTitle ?? track.Title
                    }),
                    CreatedAtUtc = DateTimeOffset.UtcNow
                };

                _context.YoutubeVideoEngagements.Add(engagement);
            }

            _context.PromptGenerations.Add(generation);

            engagement.PromptTemplateId = template.Id;
            engagement.PromptGenerationId = generation.Id;
            engagement.Provider = generation.Provider;
            engagement.Model = generation.Model;
            engagement.GeneratedText = null;
            engagement.FinalText = null;
            engagement.ErrorMessage = null;
            engagement.Status = YoutubeVideoEngagementStatus.Draft;
            engagement.UpdatedAtUtc = DateTimeOffset.UtcNow;

            await _context.SaveChangesAsync();

            await AppendJobLogAsync(jobId, "Info", $"Generating engagement track_id={track.Id} video_id={uploadedTrack.VideoId}");

            var stopwatch = Stopwatch.StartNew();
            var execution = await ExecutePromptGenerationAsync(
                generation.Provider,
                generation.Model ?? "gemini-2.5-pro",
                generation.ResolvedSystemPrompt,
                generation.ResolvedUserPrompt);
            stopwatch.Stop();

            generation.LatencyMs = (int)stopwatch.ElapsedMilliseconds;
            generation.FinishedAtUtc = DateTimeOffset.UtcNow;
            generation.TokenUsageJson = execution.UsageJson ?? "{}";
            generation.RunMetadataJson = JsonSerializer.Serialize(new
            {
                provider = generation.Provider,
                model = execution.Model,
                finishReason = execution.FinishReason
            });

            if (!execution.Success)
            {
                generation.Status = PromptGenerationStatus.Failed;
                generation.ErrorMessage = execution.ErrorMessage;

                engagement.Status = YoutubeVideoEngagementStatus.Failed;
                engagement.ErrorMessage = execution.ErrorMessage;
                engagement.UpdatedAtUtc = DateTimeOffset.UtcNow;

                await _context.SaveChangesAsync();
                await AppendJobLogAsync(jobId, "Error", $"Engagement generation failed track_id={track.Id}", execution.ErrorMessage);
                continue;
            }

            var outputText = string.IsNullOrWhiteSpace(execution.RawText)
                ? string.Empty
                : execution.RawText.Trim();

            var output = new PromptGenerationOutput
            {
                Id = Guid.NewGuid(),
                PromptGenerationId = generation.Id,
                OutputType = "text",
                OutputLabel = "Primary Output",
                OutputText = outputText,
                OutputJson = null,
                IsPrimary = true,
                IsValid = !string.IsNullOrWhiteSpace(outputText),
                ValidationErrors = string.IsNullOrWhiteSpace(outputText) ? "Prompt generation returned empty text." : null,
                ProviderResponseJson = execution.RawResponseJson,
                CreatedAtUtc = DateTimeOffset.UtcNow
            };

            generation.Outputs.Add(output);
            generation.Status = output.IsValid ? PromptGenerationStatus.Completed : PromptGenerationStatus.Failed;
            generation.ErrorMessage = output.ValidationErrors;

            engagement.GeneratedText = outputText;
            engagement.FinalText = outputText;
            engagement.ErrorMessage = output.ValidationErrors;
            engagement.Status = output.IsValid ? YoutubeVideoEngagementStatus.Generated : YoutubeVideoEngagementStatus.Failed;
            engagement.UpdatedAtUtc = DateTimeOffset.UtcNow;

            await _context.SaveChangesAsync();

            if (output.IsValid)
            {
                generatedCount++;
            }
        }

        return generatedCount;
    }

    private static string BuildYoutubeEngagementInputJson(Track track, Playlist playlist, TrackOnYoutube uploadedTrack)
    {
        var payload = new
        {
            track_title = track.Title,
            youtube_title = uploadedTrack.Title ?? track.YouTubeTitle ?? track.Title,
            genre = track.Style ?? TryGetMetadataString(track.Metadata, "styleSummary") ?? "Workout Music",
            theme = playlist.Theme ?? playlist.Title,
            tempo_bpm = track.TempoBpm,
            key = track.Key,
            energy_level = track.EnergyLevel,
            hook_type = TryGetMetadataString(track.Metadata, "hookType"),
            youtube_description = uploadedTrack.Description ?? TryGetMetadataString(track.Metadata, "youtubeDescription"),
            playlist_title = playlist.Title,
            playlist_strategy = playlist.PlaylistStrategy,
            target_audience = TryGetMetadataString(track.Metadata, "targetAudience"),
            listening_scenario = TryGetMetadataString(track.Metadata, "listeningScenario")
                ?? TryGetMetadataString(track.Metadata, "playlistCategory"),
            channel_voice = "short, sharp, confident, slightly provocative"
        };

        return JsonSerializer.Serialize(payload);
    }

    private static string RenderPromptUserTemplate(PromptTemplate template, string inputLabel, string inputJson)
    {
        var userPromptTemplate = string.IsNullOrWhiteSpace(template.UserPromptTemplate)
            ? "{{input_json}}"
            : template.UserPromptTemplate!;

        return userPromptTemplate
            .Replace("{{theme}}", inputLabel, StringComparison.Ordinal)
            .Replace("{{input_label}}", inputLabel, StringComparison.Ordinal)
            .Replace("{{input_json}}", inputJson, StringComparison.Ordinal);
    }
}
