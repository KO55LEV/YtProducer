# YtProducer.Media

`YtProducer.Media` is a .NET 8 JSON-RPC 2.0 MCP server over STDIN/STDOUT.

It provides three tools:

- `video.create_music_visualizer`
- `video.upscale`
- `media.create_youtube_thumbnail`

The tool renders a frame-based music visualizer video from:

- one source image (`.jpg`, `.jpeg`, `.png`)
- one source audio file (`.mp3`, `.wav`)

It generates:

- `analysis.json` with per-frame FFT/energy/beat data
- PNG frames in a temp job directory
- final MP4 output (`1920x1080`, `30fps` by default)

## Prerequisites

- .NET 8 SDK
- `ffmpeg` and `ffprobe` installed and available in `PATH` (or set env vars below)

## Environment Variables

- `FFMPEG_PATH` (default: `ffmpeg`)
- `FFPROBE_PATH` (default: `ffprobe`)
- `MEDIA_TMP_DIR` (default: `./tmp`)
- `MEDIA_OUTPUT_DIR` (default: `./outputs`)

Env file discovery (shared + local):

- Existing process env vars are never overwritten.
- Local scope is checked first (`.env`, then `.env.local` in current directory / app base directory).
- If no local env file is found, fallback is one-level-up `.env` (for example `Dev/.env` or `/apps/.env`).

## Run

```bash
dotnet run --project YtProducer.Media/YtProducer.Media.csproj
```

## JSON-RPC Example: tools/list

```json
{"jsonrpc":"2.0","id":1,"method":"tools/list","params":{}}
```

## JSON-RPC Example: tools/call

```json
{
  "jsonrpc": "2.0",
  "id": 2,
  "method": "tools/call",
  "params": {
    "name": "video.create_music_visualizer",
    "arguments": {
      "image_path": "/abs/path/image.jpg",
      "audio_path": "/abs/path/audio.mp3",
      "fps": 30,
      "width": 1920,
      "height": 1080,
      "gpu": true,
      "eq_bands": 64,
      "keep_temp": false,
      "temp_dir": "/abs/path/tmp",
      "output_dir": "/abs/path/outputs"
    }
  }
}
```

Upscale existing video:

```json
{
  "jsonrpc": "2.0",
  "id": 3,
  "method": "tools/call",
  "params": {
    "name": "video.upscale",
    "arguments": {
      "input_path": "/abs/path/input.mp4",
      "target_size": "FHD",
      "temp_dir": "/abs/path/tmp",
      "output_dir": "/abs/path/outputs"
    }
  }
}
```

Create YouTube thumbnail:

```json
{
  "jsonrpc": "2.0",
  "id": 4,
  "method": "tools/call",
  "params": {
    "name": "media.create_youtube_thumbnail",
    "arguments": {
      "image_path": "/abs/path/1.jpg",
      "logo_path": "/abs/path/auruz_logo.png",
      "headline": "FOCUS",
      "subheadline": "DEEP HOUSE WORKOUT",
      "output_path": "/abs/path/1_thumbnail.jpg",
      "style": {
        "headline_font": "/abs/path/fonts/BebasNeue-Bold.ttf",
        "subheadline_font": "/abs/path/fonts/Montserrat-SemiBold.ttf",
        "headline_color": "#F2FFF8",
        "subheadline_color": "#D6EAE2",
        "shadow": true,
        "stroke": true
      }
    }
  }
}
```

## Output Location

- MP4 files are written to `MEDIA_OUTPUT_DIR` (default `./outputs`).
- Per-request `output_dir` overrides `MEDIA_OUTPUT_DIR`/`Media.OutputDir`.

## Temp Job Structure

Each request creates a job folder:

```text
{MEDIA_TMP_DIR}/job-{yyyyMMdd-HHmmss}-{8charGuid}/
  analysis/
    analysis.json
  frames/
    frame_000001.png ...
  logs/
    ffmpeg_stderr.txt
```

If `keep_temp=false`, the job folder is cleaned up after completion (best effort). If `keep_temp=true`, temp files remain.
Per-request `temp_dir` overrides `MEDIA_TMP_DIR`/`Media.TempDir` for the job folder location.
