using System.Text.Json;
using YtProducer.Media.Mcp;
using YtProducer.Media.Models;
using YtProducer.Media.Services;

namespace YtProducer.Media.Tools;

public sealed class MediaCreateYoutubeThumbnailTool
{
    private readonly YoutubeThumbnailService _thumbnailService;

    public MediaCreateYoutubeThumbnailTool(YoutubeThumbnailService thumbnailService)
    {
        _thumbnailService = thumbnailService;
    }

    public async Task<CreateYoutubeThumbnailResponse> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        var request = ParseRequest(arguments);

        var imagePath = Path.GetFullPath(request.ImagePath!);
        if (!File.Exists(imagePath))
        {
            throw new ToolValidationException($"image_path does not exist: {imagePath}");
        }

        if (!string.IsNullOrWhiteSpace(request.LogoPath))
        {
            var logoPath = Path.GetFullPath(request.LogoPath);
            if (!File.Exists(logoPath))
            {
                throw new ToolValidationException($"logo_path does not exist: {logoPath}");
            }
        }

        var outputPath = Path.GetFullPath(request.OutputPath!);
        request.ImagePath = imagePath;
        request.OutputPath = outputPath;
        request.LogoPath = string.IsNullOrWhiteSpace(request.LogoPath) ? null : Path.GetFullPath(request.LogoPath);

        return await _thumbnailService.CreateAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private static CreateYoutubeThumbnailRequest ParseRequest(JsonElement arguments)
    {
        if (arguments.ValueKind is not JsonValueKind.Object)
        {
            throw new ToolValidationException("arguments must be an object.");
        }

        CreateYoutubeThumbnailRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<CreateYoutubeThumbnailRequest>(arguments.GetRawText(), JsonRpcModels.SerializerOptions);
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

        if (string.IsNullOrWhiteSpace(request.OutputPath))
        {
            throw new ToolValidationException("output_path is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Headline))
        {
            throw new ToolValidationException("headline is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Subheadline))
        {
            throw new ToolValidationException("subheadline is required.");
        }

        return request;
    }
}
