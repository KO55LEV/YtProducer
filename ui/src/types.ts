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
