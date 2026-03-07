using System.Text.Json;
using YtProducer.Media.Models;
using YtProducer.Media.Mcp;
using YtProducer.Media.Services;

namespace YtProducer.Media.Tools;

public sealed class VideoCreateMusicVisualizerTool
{
    private readonly WorkingDirectoryService _workingDirectoryService;
    private readonly AudioProbeService _audioProbeService;
    private readonly AudioAnalysisService _audioAnalysisService;
    private readonly FrameRenderService _frameRenderService;
    private readonly VideoEncodeService _videoEncodeService;

    public VideoCreateMusicVisualizerTool(
        WorkingDirectoryService workingDirectoryService,
        AudioProbeService audioProbeService,
        AudioAnalysisService audioAnalysisService,
        FrameRenderService frameRenderService,
        VideoEncodeService videoEncodeService)
    {
        _workingDirectoryService = workingDirectoryService;
        _audioProbeService = audioProbeService;
        _audioAnalysisService = audioAnalysisService;
        _frameRenderService = frameRenderService;
        _videoEncodeService = videoEncodeService;
    }

    public async Task<VideoCreateMusicVisualizerResponse> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        var request = ParseRequest(arguments);

        var imagePath = Path.GetFullPath(request.ImagePath!);
        var audioPath = Path.GetFullPath(request.AudioPath!);

        if (!File.Exists(imagePath))
        {
            throw new ToolValidationException($"image_path does not exist: {imagePath}");
        }

        if (!File.Exists(audioPath))
        {
            throw new ToolValidationException($"audio_path does not exist: {audioPath}");
        }

        EnsureSupportedImage(imagePath);
        EnsureSupportedAudio(audioPath);

        var fps = (int)(request.Fps ?? 30);
        var width = (int)(request.Width ?? 1920);
        var height = (int)(request.Height ?? 1080);
        var eqBands = (int)(request.EqBands ?? 64);
        var keepTemp = request.KeepTemp ?? false;
        var useGpu = request.Gpu ?? false;

        if (fps <= 0 || fps > 120)
        {
            throw new ToolValidationException("fps must be in range 1..120.");
        }

        if (width <= 0 || height <= 0)
        {
            throw new ToolValidationException("width and height must be > 0.");
        }

        if (eqBands < 8 || eqBands > 256)
        {
            throw new ToolValidationException("eq_bands must be in range 8..256.");
        }

        var videoBitrate = request.VideoBitrate ?? "12M";
        var audioBitrate = request.AudioBitrate ?? "320k";

        var seed = request.Seed.HasValue
            ? (int)Math.Round(request.Seed.Value, MidpointRounding.AwayFromZero)
            : DeriveSeedFromPath(audioPath);

        var job = _workingDirectoryService.CreateJobDirectory(request.TempDir, request.OutputDir);
        var analysisPath = Path.Combine(job.AnalysisDir, "analysis.json");
        var framesDir = job.FramesDir;

        var response = new VideoCreateMusicVisualizerResponse
        {
            Ok = false,
            OutputPath = string.Empty,
            DurationSeconds = 0,
            Width = width,
            Height = height,
            Fps = fps,
            AnalysisPath = analysisPath,
            FramesDir = framesDir,
            FrameCount = 0,
            FfmpegCommand = string.Empty,
            StderrTail = string.Empty,
            TempDir = keepTemp ? job.JobDir : null
        };

        try
        {
            var duration = await _audioProbeService.ProbeDurationAsync(audioPath, cancellationToken).ConfigureAwait(false);
            var analysis = await _audioAnalysisService
                .AnalyzeAsync(audioPath, duration, fps, eqBands, analysisPath, cancellationToken)
                .ConfigureAwait(false);

            await _frameRenderService
                .RenderFramesAsync(imagePath, analysis, framesDir, width, height, seed, cancellationToken)
                .ConfigureAwait(false);

            var encodeResult = await _videoEncodeService
                .EncodeAsync(
                    framesDir,
                    audioPath,
                    fps,
                    videoBitrate,
                    audioBitrate,
                    job.LogsDir,
                    job.OutputDir,
                    useGpu,
                    cancellationToken)
                .ConfigureAwait(false);

            response = response with
            {
                Ok = encodeResult.Success,
                OutputPath = encodeResult.OutputPath,
                DurationSeconds = analysis.DurationSeconds,
                FrameCount = analysis.FrameCount,
                FfmpegCommand = encodeResult.CommandLine,
                StderrTail = encodeResult.StderrTail
            };

            if (!encodeResult.Success)
            {
                response = response with { Ok = false };
            }
        }
        catch (Exception ex)
        {
            response = response with
            {
                Ok = false,
                StderrTail = AppendError(response.StderrTail, ex.Message)
            };
        }
        finally
        {
            if (!keepTemp)
            {
                _workingDirectoryService.TryCleanup(job);
                response = response with { TempDir = null };
            }
        }

        return response;
    }

    private static VideoCreateMusicVisualizerRequest ParseRequest(JsonElement arguments)
    {
        if (arguments.ValueKind is not JsonValueKind.Object)
        {
            throw new ToolValidationException("arguments must be an object.");
        }

        VideoCreateMusicVisualizerRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<VideoCreateMusicVisualizerRequest>(arguments.GetRawText(), JsonRpcModels.SerializerOptions);
        }
        catch (JsonException ex)
        {
            throw new ToolValidationException($"Invalid arguments JSON: {ex.Message}");
        }

        if (request is null)
        {
            throw new ToolValidationException("Request payload is empty.");
        }

        if (string.IsNullOrWhiteSpace(request.ImagePath))
        {
            throw new ToolValidationException("image_path is required.");
        }

        if (string.IsNullOrWhiteSpace(request.AudioPath))
        {
            throw new ToolValidationException("audio_path is required.");
        }

        return request;
    }

    private static void EnsureSupportedImage(string imagePath)
    {
        var extension = Path.GetExtension(imagePath).ToLowerInvariant();
        if (extension is not ".jpg" and not ".jpeg" and not ".png")
        {
            throw new ToolValidationException("image_path must be a JPG or PNG file.");
        }
    }

    private static void EnsureSupportedAudio(string audioPath)
    {
        var extension = Path.GetExtension(audioPath).ToLowerInvariant();
        if (extension is not ".mp3" and not ".wav")
        {
            throw new ToolValidationException("audio_path must be an MP3 or WAV file.");
        }
    }

    private static int DeriveSeedFromPath(string audioPath)
    {
        var fileName = Path.GetFileName(audioPath);
        unchecked
        {
            var hash = 17;
            foreach (var ch in fileName)
            {
                hash = hash * 31 + ch;
            }

            return hash;
        }
    }

    private static string AppendError(string existing, string error)
    {
        if (string.IsNullOrWhiteSpace(existing))
        {
            return error;
        }

        return $"{existing}{Environment.NewLine}{error}";
    }
}

public sealed class ToolValidationException : Exception
{
    public ToolValidationException(string message)
        : base(message)
    {
    }
}
