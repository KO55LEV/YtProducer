export type Track = {
  id: string;
  playlistPosition: number;
  title: string;
  youTubeTitle?: string | null;
  style?: string | null;
  duration?: string | null;
  tempoBpm?: number | null;
  key?: string | null;
  energyLevel?: number | null;
  status: string;
};

export type Playlist = {
  id: string;
  title: string;
  theme?: string | null;
  description: string | null;
  playlistStrategy?: string | null;
  status: string;
  trackCount: number;
  youtubePlaylistId?: string | null;
  createdAtUtc: string;
  publishedAtUtc?: string | null;
  tracks: Track[];
};

export type PlaylistMediaFile = {
  fileName: string;
  url: string;
};

export type PlaylistTrackMedia = {
  playlistPosition: number;
  images: PlaylistMediaFile[];
  videos: PlaylistMediaFile[];
  audios: PlaylistMediaFile[];
};

export type PlaylistMediaResponse = {
  playlistId: string;
  tracks: PlaylistTrackMedia[];
};

export type TrackVideoGeneration = {
  id: string;
  trackId: string;
  playlistId: string;
  playlistPosition: number;
  status: string;
  progressPercent: number;
  progressCurrentFrame?: number | null;
  progressTotalFrames?: number | null;
  trackDurationSeconds?: number | null;
  imagePath?: string | null;
  audioPath?: string | null;
  tempDir?: string | null;
  outputDir?: string | null;
  width?: number | null;
  height?: number | null;
  fps?: number | null;
  eqBands?: number | null;
  videoBitrate?: string | null;
  audioBitrate?: string | null;
  seed?: number | null;
  useGpu?: boolean | null;
  keepTemp?: boolean | null;
  useRawPipe?: boolean | null;
  rendererVariant?: string | null;
  outputFileNameOverride?: string | null;
  logoPath?: string | null;
  outputVideoPath?: string | null;
  analysisPath?: string | null;
  ffmpegCommand?: string | null;
  errorMessage?: string | null;
  metadata?: string | null;
  startedAtUtc?: string | null;
  finishedAtUtc?: string | null;
  createdAtUtc: string;
  updatedAtUtc: string;
};

export type TrackLoop = {
  id: string;
  playlistId: string;
  trackId: string;
  trackPosition: number;
  loopCount: number;
  status: string;
  sourceAudioPath?: string | null;
  sourceImagePath?: string | null;
  sourceVideoPath?: string | null;
  outputVideoPath?: string | null;
  thumbnailPath?: string | null;
  youtubeVideoId?: string | null;
  youtubeUrl?: string | null;
  title?: string | null;
  description?: string | null;
  metadata?: string | null;
  startedAtUtc?: string | null;
  finishedAtUtc?: string | null;
  createdAtUtc: string;
  updatedAtUtc: string;
};

export type ScheduleTrackLoopResponse = {
  jobId: string;
  jobType: string;
  loop: TrackLoop;
};

export type YoutubePlaylist = {
  id: string;
  youtubePlaylistId: string;
  title?: string | null;
  description?: string | null;
  status?: string | null;
  privacyStatus?: string | null;
  channelId?: string | null;
  channelTitle?: string | null;
  itemCount?: number | null;
  publishedAtUtc?: string | null;
  thumbnailUrl?: string | null;
  etag?: string | null;
  lastSyncedAtUtc?: string | null;
  metadata?: string | null;
  createdAtUtc: string;
  updatedAtUtc: string;
};
