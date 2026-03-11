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
  likesCount: number;
  dislikesCount: number;
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

export type AlbumReleaseTrack = {
  trackId: string;
  playlistPosition: number;
  title: string;
  duration?: string | null;
  durationSeconds: number;
  startOffsetSeconds: number;
  startOffsetLabel: string;
  previewImageUrl?: string | null;
  previewVideoUrl?: string | null;
};

export type AlbumRelease = {
  id: string;
  playlistId: string;
  status: string;
  title: string;
  description?: string | null;
  thumbnailPath?: string | null;
  thumbnailUrl?: string | null;
  outputVideoPath?: string | null;
  outputVideoUrl?: string | null;
  tempRootPath?: string | null;
  youtubeVideoId?: string | null;
  youtubeUrl?: string | null;
  tempFilesExist: boolean;
  tempFileCount: number;
  trackCount: number;
  totalDurationSeconds: number;
  thumbnailVersion: number;
  thumbnailPreviewUrls: string[];
  tracks: AlbumReleaseTrack[];
  metadata?: string | null;
  createdAtUtc: string;
  updatedAtUtc: string;
  finishedAtUtc?: string | null;
};

export type ScheduleDeleteAlbumReleaseTempFilesResponse = {
  albumReleaseId: string;
  jobId: string;
  jobType: string;
  jobStatus: string;
};

export type ScheduleAlbumReleaseJobResponse = {
  albumReleaseId: string;
  jobId: string;
  jobType: string;
  jobStatus: string;
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

export type PlaylistPromptItem = {
  playlistPosition: number;
  trackTitle: string;
  prompt: string;
};

export type PlaylistPromptResponse = {
  playlistId: string;
  promptType: string;
  sourceFileName?: string | null;
  prompts: PlaylistPromptItem[];
};

export type UpdatePlaylistStatusResponse = {
  playlistId: string;
  previousStatus: string;
  status: string;
};

export type PromptTemplate = {
  id: string;
  name: string;
  slug: string;
  purpose: string;
  description?: string | null;
  notes?: string | null;
  systemPrompt?: string | null;
  userPromptTemplate?: string | null;
  inputMode: string;
  provider: string;
  model?: string | null;
  outputMode: string;
  schemaKey?: string | null;
  settingsJson: string;
  inputContractJson: string;
  metadataJson: string;
  isActive: boolean;
  isDefault: boolean;
  sortOrder: number;
  version: number;
  createdAtUtc: string;
  updatedAtUtc: string;
};

export type PromptGenerationOutput = {
  id: string;
  promptGenerationId: string;
  outputType: string;
  outputLabel?: string | null;
  outputText?: string | null;
  outputJson?: string | null;
  isPrimary: boolean;
  isValid: boolean;
  validationErrors?: string | null;
  providerResponseJson?: string | null;
  createdAtUtc: string;
};

export type PromptGeneration = {
  id: string;
  templateId: string;
  purpose: string;
  provider: string;
  status: string;
  model?: string | null;
  inputLabel?: string | null;
  inputJson: string;
  resolvedSystemPrompt: string;
  resolvedUserPrompt: string;
  jobId?: string | null;
  latencyMs?: number | null;
  tokenUsageJson?: string | null;
  runMetadataJson?: string | null;
  targetType?: string | null;
  targetId?: string | null;
  createdAtUtc: string;
  startedAtUtc?: string | null;
  finishedAtUtc?: string | null;
  errorMessage?: string | null;
  outputs: PromptGenerationOutput[];
};

export type SchedulePromptGenerationRunResponse = {
  promptGenerationId: string;
  jobId: string;
  jobType: string;
  jobStatus: string;
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

export type TrackSocialStatResponse = {
  trackId: string;
  playlistId: string;
  likesCount: number;
  dislikesCount: number;
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

export type Job = {
  id: string;
  type: string;
  status: string;
  targetType?: string | null;
  targetId?: string | null;
  jobGroupId?: string | null;
  sequence?: number | null;
  progress: number;
  payloadJson?: string | null;
  resultJson?: string | null;
  retryCount: number;
  maxRetries: number;
  workerId?: string | null;
  leaseExpiresAt?: string | null;
  createdAt: string;
  startedAt?: string | null;
  finishedAt?: string | null;
  lastHeartbeat?: string | null;
  errorCode?: string | null;
  errorMessage?: string | null;
  idempotencyKey?: string | null;
};

export type JobLog = {
  id: string;
  jobId: string;
  level: string;
  message: string;
  metadata?: string | null;
  createdAtUtc: string;
};

export type YoutubeVideoEngagement = {
  id: string;
  channelId: string;
  youtubeVideoId: string;
  trackId?: string | null;
  playlistId?: string | null;
  albumReleaseId?: string | null;
  engagementType: string;
  promptTemplateId?: string | null;
  promptGenerationId?: string | null;
  provider?: string | null;
  model?: string | null;
  generatedText?: string | null;
  finalText?: string | null;
  status: string;
  youtubeCommentId?: string | null;
  postedAtUtc?: string | null;
  errorMessage?: string | null;
  metadataJson: string;
  createdAtUtc: string;
  updatedAtUtc: string;
};
