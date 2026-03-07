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
