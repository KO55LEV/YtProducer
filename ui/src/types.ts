export type Track = {
  id: string;
  title: string;
  status: string;
  sortOrder: number;
};

export type Playlist = {
  id: string;
  title: string;
  description: string | null;
  status: string;
  tracks: Track[];
};
