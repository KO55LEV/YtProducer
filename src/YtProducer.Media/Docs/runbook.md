# YtProducer.Media Runbook

Shared env file can be placed at `Dev/.env` (or `/apps/.env` on VPS). Optional local overrides can be in `.env`/`.env.local` near the project.

## Local Build

```bash
cd /Volumes/Data/Devs/Projects/SkydersLab/Dev
dotnet build YtProducer.Media/YtProducer.Media.csproj
```

## Quick Smoke Test (Faster)

Uses your local test media and keeps temp files for inspection.

```bash
dotnet run --project YtProducer.Media/YtProducer.Media.csproj << 'EOF'
{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"video.create_music_visualizer","arguments":{"image_path":"/Volumes/Data/Devs/Projects/test1/Gemini_Generated_Image_xf39hxxf39hxxf39.png","audio_path":"/Volumes/Data/Devs/Projects/test1/Blood, Sweat & Tears.mp3","fps":12,"width":960,"height":540,"eq_bands":32,"video_bitrate":"4M","audio_bitrate":"192k","keep_temp":true}}}
EOF
```

## Full Render (Default Quality)

Defaults: 1920x1080, 30fps, 64 EQ bands, 12M video bitrate, 320k audio bitrate.

```bash
dotnet run --project YtProducer.Media/YtProducer.Media.csproj << 'EOF'
{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"video.create_music_visualizer","arguments":{"image_path":"/Volumes/Data/Devs/Projects/test1/Gemini_Generated_Image_xf39hxxf39hxxf39.png","audio_path":"/Volumes/Data/Devs/Projects/test1/Blood, Sweat & Tears.mp3","keep_temp":true,"temp_dir":"/Volumes/SS2TBSND/MusicTesting/tmp","output_dir":"/Volumes/SS2TBSND/MusicTesting/output"}}}
EOF

dotnet run --project YtProducer.Media/YtProducer.Media.csproj << 'EOF'
{"jsonrpc":"2.0","id":5,"method":"tools/call","params":{"name":"media.create_youtube_thumbnail","arguments":{"image_path":"/Volumes/Data/Devs/Projects/test1/1.jpg","logo_path":"/Volumes/Data/Devs/Projects/test1/auruz_logo.png","headline":"FOCUS","subheadline":"DEEP HOUSE WORKOUT","output_path":"/Volumes/Data/Devs/Projects/test1/1_thumbnail.jpg","style":{"headline_font":"/Volumes/Data/Devs/Projects/test1/fonts/BebasNeue-Bold.ttf","subheadline_font":"/Volumes/Data/Devs/Projects/test1/fonts/Montserrat-SemiBold.ttf","headline_color":"#F2FFF8","subheadline_color":"#D6EAE2","shadow":true,"stroke":true}}}
EOF
```

## Upscale Existing Video To FHD Or 4K

Input video can be any local path. Output is written to configured `OutputDir` (or per-request `output_dir`) with suffix:

- `*_FHD.mp4` for `FHD`
- `*_4K.mp4` for `4K`

Audio is copied without re-encoding (`-c:a copy`), preserving original audio quality.

### FHD Example

```bash
dotnet run --project YtProducer.Media/YtProducer.Media.csproj << 'EOF'
{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"video.upscale","arguments":{"input_path":"/Volumes/Data/Devs/Projects/test1/20260303-181929-772eb192.mp4","target_size":"FHD"}}}
EOF
```

### 4K Example

```bash
dotnet run --project YtProducer.Media/YtProducer.Media.csproj << 'EOF'
{"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"video.upscale","arguments":{"input_path":"/Volumes/Data/Devs/Projects/test1/20260303-181929-772eb192.mp4","target_size":"4K"}}}
EOF
```

## Paths

The app reads `appsettings.json` (`Media.TempDir`, `Media.OutputDir`) unless overridden by env vars or per-request inputs:

- `MEDIA_TMP_DIR`
- `MEDIA_OUTPUT_DIR`
- `temp_dir` (per-request input override)
- `output_dir` (per-request input override)

With your current setup:

- Temp: `/Volumes/SS2TBSND/MusicTesting/tmp`
- Output: `/Volumes/SS2TBSND/MusicTesting/output`

## Expected Artifacts

Per job temp folder:

- `analysis/analysis.json`
- `frames/frame_000001.png` ...
- `logs/ffmpeg_stderr.txt`

Final MP4 file:

- written to configured output directory (or per-request `output_dir`)


UPSCALE TO FHD 

dotnet run --project YtProducer.Media.csproj << 'EOF'
{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"video.upscale","arguments":{"input_path":"/Volumes/Data/Devs/Projects/test1/20260303-181929-772eb192.mp4","target_size":"FHD"}}}
EOF



GENERATE VIDEO FROM MUSIC AND IMAGE 

dotnet run --project  YtProducer.Media.csproj << 'EOF'
{"jsonrpc":"2.0","id":10,"method":"tools/call","params":{"name":"video.create_music_visualizer","arguments":{"image_path":"/Volumes/Data/Devs/Projects/test1/Gemini_Generated_Image_szra4iszra4iszra.png","audio_path":"/Volumes/Data/Devs/Projects/test1/disco-90s.mp3","width":1920,"height":1080,"fps":30,"eq_bands":64,"video_bitrate":"12M","audio_bitrate":"320k","keep_temp":true}}}
EOF

cd /srv/ytmusic/app/YtProducer.Media

dotnet run << 'EOF'
{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"video.create_music_visualizer","arguments":{"image_path":"/srv/ytmusic/input/input-to-delete/image.png","audio_path":"/srv/ytmusic/input/input-to-delete/disco-90s.mp3","width":1920,"height":1080,"fps":30,"eq_bands":64,"video_bitrate":"12M","audio_bitrate":"320k","keep_temp":true,"gpu":true}}}
EOF
