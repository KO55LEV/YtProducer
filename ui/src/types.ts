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
  createdAtUtc: string;
  publishedAtUtc?: string | null;
  tracks: Track[];
};
