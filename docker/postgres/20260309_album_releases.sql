CREATE TABLE IF NOT EXISTS album_releases (
    id uuid PRIMARY KEY,
    playlist_id uuid NOT NULL REFERENCES playlists(id) ON DELETE CASCADE,
    status varchar(32) NOT NULL DEFAULT 'Draft',
    title varchar(255),
    description varchar(5000),
    thumbnail_path varchar(2000),
    output_video_path varchar(2000),
    temp_root_path varchar(2000),
    youtube_video_id varchar(128),
    youtube_url varchar(1000),
    metadata jsonb,
    finished_at_utc timestamptz,
    created_at_utc timestamptz NOT NULL DEFAULT NOW(),
    updated_at_utc timestamptz NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS ix_album_releases_playlist_id ON album_releases(playlist_id);
CREATE INDEX IF NOT EXISTS ix_album_releases_status ON album_releases(status);
CREATE INDEX IF NOT EXISTS ix_album_releases_created_at_utc ON album_releases(created_at_utc DESC);
CREATE INDEX IF NOT EXISTS ix_album_releases_metadata ON album_releases USING gin(metadata);
