namespace YtProducer.Media.Services;

public sealed class VideoUpscaleService
{
    private readonly string _ffmpegPath;
    private readonly FfmpegRunner _runner;
    private readonly string _outputRoot;

    public VideoUpscaleService(string ffmpegPath, FfmpegRunner runner, string outputRoot)
    {
        _ffmpegPath = ffmpegPath;
        _runner = runner;
        _outputRoot = Path.GetFullPath(outputRoot);

        Directory.CreateDirectory(_outputRoot);
    }

    public async Task<VideoUpscaleResult> UpscaleAsync(
        string inputPath,
        string targetSize,
        string? tempDirOverride,
        string? outputDirOverride,
        CancellationToken cancellationToken)
    {
        if (!TryParseTargetSize(targetSize, out var width, out var height, out var label))
        {
            throw new ArgumentException("targetSize must be one of: FHD, 4K.", nameof(targetSize));
        }

        var outputRoot = ResolveOutputRoot(outputDirOverride);
        var workingDir = ResolveWorkingDir(tempDirOverride);

        Directory.CreateDirectory(outputRoot);
        if (workingDir is not null)
        {
            Directory.CreateDirectory(workingDir);
        }

        var outputPath = BuildOutputPath(inputPath, label, outputRoot);

        var args = new List<string>
        {
            "-y",
            "-i", inputPath,
            "-vf", $"scale={width}:{height}:flags=lanczos",
            "-c:v", "libx264",
            "-profile:v", "high",
            "-pix_fmt", "yuv420p",
            "-b:v", label == "4K" ? "35M" : "12M",
            "-movflags", "+faststart",
            "-c:a", "copy",
            outputPath
        };

        var run = await _runner
            .RunAsync(_ffmpegPath, args, workingDirectory: workingDir, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var stderrTail = Tail(run.StdErr, 8000);

        return new VideoUpscaleResult(
            run.ExitCode == 0 && File.Exists(outputPath),
            outputPath,
            label,
            width,
            height,
            run.CommandLine,
            stderrTail);
    }

    public static bool TryParseTargetSize(string? targetSize, out int width, out int height, out string label)
    {
        width = 0;
        height = 0;
        label = string.Empty;

        if (string.IsNullOrWhiteSpace(targetSize))
        {
            return false;
        }

        if (string.Equals(targetSize, "FHD", StringComparison.OrdinalIgnoreCase))
        {
            width = 1920;
            height = 1080;
            label = "FHD";
            return true;
        }

        if (string.Equals(targetSize, "4K", StringComparison.OrdinalIgnoreCase))
        {
            width = 3840;
            height = 2160;
            label = "4K";
            return true;
        }

        return false;
    }

    private static string BuildOutputPath(string inputPath, string label, string outputRoot)
    {
        var inputName = Path.GetFileNameWithoutExtension(inputPath);
        var extension = Path.GetExtension(inputPath);
        if (string.IsNullOrWhiteSpace(extension))
        {
            extension = ".mp4";
        }

        var baseName = $"{inputName}_{label}";
        var candidate = Path.Combine(outputRoot, baseName + extension);
        if (!File.Exists(candidate))
        {
            return candidate;
        }

        for (var i = 2; i < 10000; i++)
        {
            var withSuffix = Path.Combine(outputRoot, $"{baseName}_{i}{extension}");
            if (!File.Exists(withSuffix))
            {
                return withSuffix;
            }
        }

        return Path.Combine(outputRoot, $"{baseName}_{DateTime.UtcNow:yyyyMMddHHmmssfff}{extension}");
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

    private static string? ResolveWorkingDir(string? overrideDir)
    {
        if (string.IsNullOrWhiteSpace(overrideDir))
        {
            return null;
        }

        return Path.GetFullPath(overrideDir);
    }
}

public sealed record VideoUpscaleResult(
    bool Success,
    string OutputPath,
    string TargetLabel,
    int Width,
    int Height,
    string CommandLine,
    string StderrTail);
