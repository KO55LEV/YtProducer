v1: unchanged (PNG frames on disk + encode)
v2: unchanged fast profile (720p/32 bands)
v3: new no-PNG pipeline
renders frames in memory
streams raw RGBA directly to ffmpeg stdin
encodes immediately (no frame_*.png files)
still supports GPU codec preference with fallback to libx264