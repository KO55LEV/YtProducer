namespace YtProducer.Media.Services;

public sealed class VideoEncodeService
{
    private readonly string _ffmpegPath;
    private readonly FfmpegRunner _runner;
    private readonly string _outputRoot;

    public VideoEncodeService(string ffmpegPath, FfmpegRunner runner, string outputRoot)
    {
        _ffmpegPath = ffmpegPath;
        _runner = runner;
        _outputRoot = Path.GetFullPath(outputRoot);

        Directory.CreateDirectory(_outputRoot);
    }

    public async Task<VideoEncodeResult> EncodeAsync(
        string framesDir,
        string audioPath,
        int fps,
        string videoBitrate,
        string audioBitrate,
        string logsDir,
        string? outputDirOverride,
        bool useGpu,
        CancellationToken cancellationToken)
    {
        var outputRoot = ResolveOutputRoot(outputDirOverride);

        Directory.CreateDirectory(outputRoot);
        Directory.CreateDirectory(logsDir);

        var outputFile = Path.Combine(
            outputRoot,
            $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..8]}.mp4");

        var stderrFile = Path.Combine(logsDir, "ffmpeg_stderr.txt");
        var codecs = GetPreferredCodecs(useGpu);

        FfmpegRunResult? lastRun = null;
        string? selectedCodec = null;
        var usedGpuCodec = false;
        var fallbacks = new List<string>();

        foreach (var codec in codecs)
        {
            var args = BuildArgs(codec, framesDir, audioPath, outputFile, fps, videoBitrate, audioBitrate);
            var run = await _runner
                .RunAsync(_ffmpegPath, args, stderrFilePath: stderrFile, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            lastRun = run;

            if (run.ExitCode == 0 && File.Exists(outputFile))
            {
                selectedCodec = codec;
                usedGpuCodec = !string.Equals(codec, "libx264", StringComparison.Ordinal);
                break;
            }

            fallbacks.Add(codec);
        }

        if (lastRun is null)
        {
            return new VideoEncodeResult(false, outputFile, string.Empty, "No encoding attempts were made.", stderrFile);
        }

        var success = selectedCodec is not null;
        var stderrTail = Tail(lastRun.StdErr, 8000);
        var commandLine = lastRun.CommandLine;

        if (success && useGpu && !usedGpuCodec)
        {
            var failedGpuCodecs = string.Join(", ", fallbacks.Where(c => !string.Equals(c, "libx264", StringComparison.Ordinal)));
            if (!string.IsNullOrWhiteSpace(failedGpuCodecs))
            {
                stderrTail = $"GPU requested; hardware encode unavailable ({failedGpuCodecs}). Fell back to libx264.{Environment.NewLine}{stderrTail}";
            }
        }

        return new VideoEncodeResult(success, outputFile, commandLine, stderrTail, stderrFile);
    }

    private static IReadOnlyList<string> GetPreferredCodecs(bool useGpu)
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

    private static List<string> BuildArgs(
        string codec,
        string framesDir,
        string audioPath,
        string outputFile,
        int fps,
        string videoBitrate,
        string audioBitrate)
    {
        var args = new List<string>
        {
            "-y",
            "-framerate", fps.ToString(),
            "-i", Path.Combine(framesDir, "frame_%06d.png"),
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
        args.Add(outputFile);

        return args;
    }

    private static string Tail(string text, int maxChars)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxChars)
        {
            return text;
        }

        return text[^maxChars..];
    }

    private string ResolveOutputRoot(string? overrideDir)
    {
        if (string.IsNullOrWhiteSpace(overrideDir))
        {
            return _outputRoot;
        }

        return Path.GetFullPath(overrideDir);
    }
}

public sealed record VideoEncodeResult(
    bool Success,
    string OutputPath,
    string CommandLine,
    string StderrTail,
    string StderrFilePath);
