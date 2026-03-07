using System.Text.Json.Nodes;

namespace YtProducer.Media.Mcp;

public static class ToolSchemas
{
    public const string VisualizerToolName = "video.create_music_visualizer";
    public const string UpscaleToolName = "video.upscale";
    public const string CreateYoutubeThumbnailToolName = "media.create_youtube_thumbnail";

    public static JsonNode VisualizerInputSchema => Parse("""
    {
      "type":"object",
      "properties":{
        "image_path":{"type":"string"},
        "audio_path":{"type":"string"},
        "seed":{"type":["number","null"]},
        "fps":{"type":["number","null"],"default":30},
        "width":{"type":["number","null"],"default":1920},
        "height":{"type":["number","null"],"default":1080},
        "video_bitrate":{"type":["string","null"],"default":"12M"},
        "audio_bitrate":{"type":["string","null"],"default":"320k"},
        "eq_bands":{"type":["number","null"],"default":64},
        "keep_temp":{"type":["boolean","null"],"default":false},
        "gpu":{"type":["boolean","null"],"default":false},
        "temp_dir":{"type":["string","null"]},
        "output_dir":{"type":["string","null"]}
      },
      "required":["image_path","audio_path"],
      "additionalProperties":false
    }
    """);

    public static JsonNode VisualizerOutputSchema => Parse("""
    {
      "type":"object",
      "properties":{
        "ok":{"type":"boolean"},
        "output_path":{"type":"string"},
        "duration_seconds":{"type":"number"},
        "width":{"type":"number"},
        "height":{"type":"number"},
        "fps":{"type":"number"},
        "analysis_path":{"type":"string"},
        "frames_dir":{"type":"string"},
        "frame_count":{"type":"number"},
        "ffmpeg_command":{"type":"string"},
        "stderr_tail":{"type":"string"},
        "temp_dir":{"type":["string","null"]}
      },
      "required":["ok","output_path","duration_seconds","width","height","fps","analysis_path","frames_dir","frame_count","ffmpeg_command","stderr_tail","temp_dir"],
      "additionalProperties":false
    }
    """);

    public static JsonNode UpscaleInputSchema => Parse("""
    {
      "type":"object",
      "properties":{
        "input_path":{"type":"string"},
        "target_size":{"type":"string","enum":["FHD","4K"]},
        "temp_dir":{"type":["string","null"]},
        "output_dir":{"type":["string","null"]}
      },
      "required":["input_path","target_size"],
      "additionalProperties":false
    }
    """);

    public static JsonNode UpscaleOutputSchema => Parse("""
    {
      "type":"object",
      "properties":{
        "ok":{"type":"boolean"},
        "input_path":{"type":"string"},
        "output_path":{"type":"string"},
        "target_size":{"type":"string"},
        "width":{"type":"number"},
        "height":{"type":"number"},
        "ffmpeg_command":{"type":"string"},
        "stderr_tail":{"type":"string"}
      },
      "required":["ok","input_path","output_path","target_size","width","height","ffmpeg_command","stderr_tail"],
      "additionalProperties":false
    }
    """);

    public static JsonNode CreateYoutubeThumbnailInputSchema => Parse("""
    {
      "type":"object",
      "properties":{
        "image_path":{"type":"string"},
        "logo_path":{"type":["string","null"]},
        "headline":{"type":"string"},
        "subheadline":{"type":"string"},
        "output_path":{"type":"string"},
        "style":{
          "type":["object","null"],
          "properties":{
            "headline_font":{"type":["string","null"]},
            "subheadline_font":{"type":["string","null"]},
            "headline_color":{"type":["string","null"]},
            "subheadline_color":{"type":["string","null"]},
            "shadow":{"type":["boolean","null"]},
            "stroke":{"type":["boolean","null"]}
          },
          "additionalProperties":false
        }
      },
      "required":["image_path","headline","subheadline","output_path"],
      "additionalProperties":false
    }
    """);

    public static JsonNode CreateYoutubeThumbnailOutputSchema => Parse("""
    {
      "type":"object",
      "properties":{
        "ok":{"type":"boolean"},
        "output_path":{"type":"string"},
        "layout":{
          "type":"object",
          "properties":{
            "headline_box":{"type":"array","items":{"type":"number"},"minItems":4,"maxItems":4},
            "subheadline_box":{"type":"array","items":{"type":"number"},"minItems":4,"maxItems":4},
            "logo_box":{"type":"array","items":{"type":"number"},"minItems":4,"maxItems":4},
            "safe_subject_mask_score":{"type":"number"}
          },
          "required":["headline_box","subheadline_box","logo_box","safe_subject_mask_score"],
          "additionalProperties":false
        }
      },
      "required":["ok","output_path","layout"],
      "additionalProperties":false
    }
    """);

    public static IReadOnlyList<object> ToDescriptors()
    {
        return
        [
            new
            {
                name = VisualizerToolName,
                description = "Render an advanced frame-based music visualizer MP4 from a single image and an audio track.",
                inputSchema = VisualizerInputSchema,
                outputSchema = VisualizerOutputSchema
            },
            new
            {
                name = UpscaleToolName,
                description = "Upscale an existing video to FHD (1920x1080) or 4K (3840x2160) and preserve original audio quality.",
                inputSchema = UpscaleInputSchema,
                outputSchema = UpscaleOutputSchema
            },
            new
            {
                name = CreateYoutubeThumbnailToolName,
                description = "Create a YouTube thumbnail by blending headline, subheadline and optional logo onto an image with saliency-aware layout.",
                inputSchema = CreateYoutubeThumbnailInputSchema,
                outputSchema = CreateYoutubeThumbnailOutputSchema
            }
        ];
    }

    private static JsonNode Parse(string json)
    {
        return JsonNode.Parse(json) ?? throw new InvalidOperationException("Failed to parse schema JSON.");
    }
}
