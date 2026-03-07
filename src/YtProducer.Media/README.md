# YtProducer.Media

`YtProducer.Media` is a .NET 8 media-processing library.

The project contains reusable services for:

- audio analysis and probing
- frame rendering
- FFmpeg orchestration
- video encoding and upscaling
- YouTube thumbnail compositing

This project intentionally does not include MCP/JSON-RPC server wrappers.

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

## Video Generation Pipeline

Use this sequence when generating a visualizer video directly via `YtProducer.Media` services:

1. `WorkingDirectoryService.CreateJobDirectory(...)`
- Creates isolated per-run folders (job root + `frames` + `analysis` + `logs`).
- Resolves temp/output overrides into absolute paths.
- Prevents collisions between parallel runs.

2. `AudioProbeService.ProbeDurationAsync(audioPath, ct)`
- Calls `ffprobe` to read exact audio duration in seconds.
- This duration drives frame count (`duration * fps`) for full-length render.

3. `AudioAnalysisService.AnalyzeAsync(audioPath, duration, fps, eqBands, analysisPath, ct)`
- Uses `ffmpeg` to decode audio to mono PCM.
- Runs FFT windowing per frame timepoint.
- Compresses spectrum into `eqBands` (bass/mid/high + total energy).
- Detects beat peaks.
- Writes `analysis.json` used by renderer and debugging.

4. `FrameRenderService.RenderFramesAsync(imagePath, analysis, framesDir, width, height, seed, ct)`
- Loads source image, applies cover/crop to target size.
- For each frame, uses analysis values to animate:
- zoom/pulse/shake
- equalizer bars
- particles/glow/vignette/grain
- Writes PNG sequence (`frame_000001.png`, ...).

5. `VideoEncodeService.EncodeAsync(framesDir, audioPath, fps, videoBitrate, audioBitrate, logsDir, outputDirOverride, useGpu, ct)`
- Calls `ffmpeg` to combine frames + original audio into MP4.
- Uses requested bitrate/fps/audio settings.
- Tries GPU codec if enabled, falls back to `libx264` if needed.
- Writes output file and stderr log file.

6. `WorkingDirectoryService.TryCleanup(...)` when `keep_temp=false`
- Deletes temporary job folders (frames, analysis artifacts, intermediates).
- Keeps final MP4 in output directory.
- Best-effort cleanup (does not fail whole run if delete fails).
