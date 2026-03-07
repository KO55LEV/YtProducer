using System.Text.Json;
using YtProducer.Media.Mcp;
using YtProducer.Media.Services;
using YtProducer.Media.Tools;

namespace YtProducer.Media;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        LoadDotEnvIfPresent();
        var appSettings = LoadAppSettings();

        var ffmpegPath = GetNonEmptyOrFallback(
            Environment.GetEnvironmentVariable("FFMPEG_PATH"),
            appSettings.FfmpegPath,
            "ffmpeg");
        var ffprobePath = GetNonEmptyOrFallback(
            Environment.GetEnvironmentVariable("FFPROBE_PATH"),
            appSettings.FfprobePath,
            "ffprobe");
        var tempRoot = Path.GetFullPath(GetNonEmptyOrFallback(
            Environment.GetEnvironmentVariable("MEDIA_TMP_DIR"),
            appSettings.TempDir,
            "./tmp"));
        var outputRoot = Path.GetFullPath(GetNonEmptyOrFallback(
            Environment.GetEnvironmentVariable("MEDIA_OUTPUT_DIR"),
            appSettings.OutputDir,
            "./outputs"));

        Directory.CreateDirectory(tempRoot);
        Directory.CreateDirectory(outputRoot);

        var ffmpegRunner = new FfmpegRunner();
        var workingDirectoryService = new WorkingDirectoryService(tempRoot, outputRoot);
        var audioProbeService = new AudioProbeService(ffprobePath, ffmpegRunner);
        var audioAnalysisService = new AudioAnalysisService(ffmpegPath, ffmpegRunner);
        var frameRenderService = new FrameRenderService(ffmpegPath, ffprobePath, ffmpegRunner);
        var videoEncodeService = new VideoEncodeService(ffmpegPath, ffmpegRunner, outputRoot);
        var videoUpscaleService = new VideoUpscaleService(ffmpegPath, ffmpegRunner, outputRoot);
        var youtubeThumbnailService = new YoutubeThumbnailService();

        var visualizerTool = new VideoCreateMusicVisualizerTool(
            workingDirectoryService,
            audioProbeService,
            audioAnalysisService,
            frameRenderService,
            videoEncodeService);

        var upscaleTool = new VideoUpscaleTool(videoUpscaleService);
        var youtubeThumbnailTool = new MediaCreateYoutubeThumbnailTool(youtubeThumbnailService);

        var server = new McpServer(visualizerTool, upscaleTool, youtubeThumbnailTool);

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cts.Cancel();
        };

        try
        {
            await server.RunAsync(cts.Token).ConfigureAwait(false);
            return 0;
        }
        catch (Exception ex)
        {
            var error = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = (object?)null,
                error = new
                {
                    code = JsonRpcError.InternalErrorCode,
                    message = "Unhandled server exception.",
                    data = ex.Message
                }
            }, JsonRpcModels.SerializerOptions);

            await Console.Out.WriteLineAsync(error).ConfigureAwait(false);
            await Console.Out.FlushAsync().ConfigureAwait(false);
            return 1;
        }
    }

    private static void LoadDotEnvIfPresent()
    {
        var protectedKeys = Environment.GetEnvironmentVariables()
            .Keys
            .Cast<object>()
            .Select(key => key?.ToString())
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Select(key => key!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in GetDotEnvCandidates())
        {
            if (!File.Exists(candidate))
            {
                continue;
            }

            ApplyDotEnv(candidate, protectedKeys);
        }
    }

    private static IReadOnlyList<string> GetDotEnvCandidates()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var localCandidates = new List<string>();
        var fallbackCandidates = new List<string>();

        static void AddCandidate(HashSet<string> seenPaths, List<string> list, string path)
        {
            var fullPath = Path.GetFullPath(path);
            if (seenPaths.Add(fullPath))
            {
                list.Add(fullPath);
            }
        }

        static void AddLocalAndParentCandidates(
            HashSet<string> seenPaths,
            List<string> localList,
            List<string> fallbackList,
            string directory)
        {
            var fullDirectory = Path.GetFullPath(directory);
            AddCandidate(seenPaths, localList, Path.Combine(fullDirectory, ".env"));
            AddCandidate(seenPaths, localList, Path.Combine(fullDirectory, ".env.local"));

            var parentDirectory = Directory.GetParent(fullDirectory)?.FullName;
            if (parentDirectory is not null)
            {
                AddCandidate(seenPaths, fallbackList, Path.Combine(parentDirectory, ".env"));
            }
        }

        AddLocalAndParentCandidates(seen, localCandidates, fallbackCandidates, Directory.GetCurrentDirectory());
        AddLocalAndParentCandidates(seen, localCandidates, fallbackCandidates, AppContext.BaseDirectory);

        var existingLocal = localCandidates.Where(File.Exists).ToList();
        if (existingLocal.Count > 0)
        {
            return existingLocal;
        }

        return fallbackCandidates.Where(File.Exists).ToList();
    }

    private static void ApplyDotEnv(string path, IReadOnlySet<string> protectedKeys)
    {
        foreach (var line in File.ReadLines(path))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith('#'))
            {
                continue;
            }

            var separator = trimmed.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var key = trimmed[..separator].Trim();
            var value = trimmed[(separator + 1)..].Trim();

            if (value.Length >= 2 &&
                ((value.StartsWith('"') && value.EndsWith('"')) ||
                 (value.StartsWith('\'') && value.EndsWith('\''))))
            {
                value = value[1..^1];
            }

            if (string.IsNullOrWhiteSpace(key) || protectedKeys.Contains(key))
            {
                continue;
            }

            Environment.SetEnvironmentVariable(key, value);
        }
    }

    private static AppSettings LoadAppSettings()
    {
        foreach (var path in GetAppSettingsCandidates())
        {
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(path));
                var root = document.RootElement;
                if (root.ValueKind is not JsonValueKind.Object)
                {
                    continue;
                }

                if (TryGetObjectProperty(root, "Media", out var media))
                {
                    return ReadAppSettingsFrom(media);
                }

                return ReadAppSettingsFrom(root);
            }
            catch (JsonException)
            {
                // Ignore malformed appsettings and continue with defaults.
            }
            catch (IOException)
            {
                // Ignore inaccessible files and continue with defaults.
            }
        }

        return new AppSettings(null, null, null, null);
    }

    private static IReadOnlyList<string> GetAppSettingsCandidates()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var candidates = new List<string>();

        static void AddCandidate(HashSet<string> seenPaths, List<string> list, string path)
        {
            var fullPath = Path.GetFullPath(path);
            if (seenPaths.Add(fullPath))
            {
                list.Add(fullPath);
            }
        }

        AddCandidate(seen, candidates, Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json"));
        AddCandidate(seen, candidates, Path.Combine(AppContext.BaseDirectory, "appsettings.json"));

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var depth = 0; depth < 8 && dir is not null; depth++)
        {
            AddCandidate(seen, candidates, Path.Combine(dir.FullName, "appsettings.json"));
            dir = dir.Parent;
        }

        return candidates;
    }

    private static AppSettings ReadAppSettingsFrom(JsonElement source)
    {
        return new AppSettings(
            GetStringProperty(source, "FfmpegPath"),
            GetStringProperty(source, "FfprobePath"),
            GetStringProperty(source, "TempDir"),
            GetStringProperty(source, "OutputDir"));
    }

    private static bool TryGetObjectProperty(JsonElement source, string name, out JsonElement value)
    {
        foreach (var property in source.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase) &&
                property.Value.ValueKind is JsonValueKind.Object)
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string? GetStringProperty(JsonElement source, string name)
    {
        foreach (var property in source.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase) &&
                property.Value.ValueKind is JsonValueKind.String)
            {
                return property.Value.GetString();
            }
        }

        return null;
    }

    private static string GetNonEmptyOrFallback(string? primary, string? secondary, string fallback)
    {
        if (!string.IsNullOrWhiteSpace(primary))
        {
            return primary.Trim();
        }

        if (!string.IsNullOrWhiteSpace(secondary))
        {
            return secondary.Trim();
        }

        return fallback;
    }

    private sealed record AppSettings(
        string? FfmpegPath,
        string? FfprobePath,
        string? TempDir,
        string? OutputDir);
}
