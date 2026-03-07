using System.Text.Json;
using YtProducer.Media.Models;
using YtProducer.Media.Mcp;
using YtProducer.Media.Services;

namespace YtProducer.Media.Tools;

public sealed class VideoUpscaleTool
{
    private readonly VideoUpscaleService _upscaleService;

    public VideoUpscaleTool(VideoUpscaleService upscaleService)
    {
        _upscaleService = upscaleService;
    }

    public async Task<VideoUpscaleResponse> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        var request = ParseRequest(arguments);
        var inputPath = Path.GetFullPath(request.InputPath!);

        if (!File.Exists(inputPath))
        {
            throw new ToolValidationException($"input_path does not exist: {inputPath}");
        }

        if (!VideoUpscaleService.TryParseTargetSize(request.TargetSize, out _, out _, out _))
        {
            throw new ToolValidationException("target_size must be one of: FHD, 4K.");
        }

        var result = await _upscaleService
            .UpscaleAsync(inputPath, request.TargetSize!, request.TempDir, request.OutputDir, cancellationToken)
            .ConfigureAwait(false);

        return new VideoUpscaleResponse
        {
            Ok = result.Success,
            InputPath = inputPath,
            OutputPath = result.OutputPath,
            TargetSize = result.TargetLabel,
            Width = result.Width,
            Height = result.Height,
            FfmpegCommand = result.CommandLine,
            StderrTail = result.StderrTail
        };
    }

    private static VideoUpscaleRequest ParseRequest(JsonElement arguments)
    {
        if (arguments.ValueKind is not JsonValueKind.Object)
        {
            throw new ToolValidationException("arguments must be an object.");
        }

        VideoUpscaleRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<VideoUpscaleRequest>(arguments.GetRawText(), JsonRpcModels.SerializerOptions);
        }
        catch (JsonException ex)
        {
            throw new ToolValidationException($"Invalid arguments JSON: {ex.Message}");
        }

        if (request is null)
        {
            throw new ToolValidationException("Request payload is empty.");
        }

        if (string.IsNullOrWhiteSpace(request.InputPath))
        {
            throw new ToolValidationException("input_path is required.");
        }

        if (string.IsNullOrWhiteSpace(request.TargetSize))
        {
            throw new ToolValidationException("target_size is required (FHD or 4K).");
        }

        return request;
    }
}
