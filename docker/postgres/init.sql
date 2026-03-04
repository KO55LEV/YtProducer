CREATE TABLE IF NOT EXISTS playlists (
    id uuid PRIMARY KEY,
    title varchar(255) NOT NULL,
    theme varchar(200),
    description varchar(2000),
    playlist_strategy text,
    status varchar(32) NOT NULL DEFAULT 'Draft',
    track_count integer DEFAULT 0,
    metadata jsonb,
    created_at_utc timestamptz NOT NULL DEFAULT NOW(),
    updated_at_utc timestamptz NOT NULL DEFAULT NOW(),
    published_at_utc timestamptz
);

CREATE INDEX IF NOT EXISTS ix_playlists_status ON playlists(status);
CREATE INDEX IF NOT EXISTS ix_playlists_theme ON playlists(theme);
CREATE INDEX IF NOT EXISTS ix_playlists_created_at ON playlists(created_at_utc DESC);
CREATE INDEX IF NOT EXISTS ix_playlists_metadata ON playlists USING gin(metadata);

CREATE TABLE IF NOT EXISTS tracks (
    id uuid PRIMARY KEY,
    playlist_id uuid NOT NULL REFERENCES playlists(id) ON DELETE CASCADE,
    playlist_position integer NOT NULL,
    title varchar(200) NOT NULL,
    youtube_title varchar(200),
    source_url varchar(1000),
    style varchar(100),
    duration varchar(20),
    tempo_bpm integer,
    key varchar(50),
    energy_level integer,
    status varchar(32) NOT NULL DEFAULT 'Pending',
    
    -- Rich metadata as JSONB for flexibility
    metadata jsonb,
    
    created_at_utc timestamptz NOT NULL DEFAULT NOW(),
    updated_at_utc timestamptz NOT NULL DEFAULT NOW(),
    
    CONSTRAINT uq_tracks_playlist_position UNIQUE (playlist_id, playlist_position)
);

CREATE INDEX IF NOT EXISTS ix_tracks_playlist_id ON tracks(playlist_id);
CREATE INDEX IF NOT EXISTS ix_tracks_status ON tracks(status);

CREATE TABLE IF NOT EXISTS jobs (
    id uuid PRIMARY KEY,
    playlist_id uuid NOT NULL REFERENCES playlists(id) ON DELETE CASCADE,
    type varchar(32) NOT NULL,
    status varchar(32) NOT NULL,
    payload_json jsonb NOT NULL,
    attempts integer NOT NULL,
    created_at_utc timestamptz NOT NULL,
    started_at_utc timestamptz,
    completed_at_utc timestamptz,
    error_message varchar(2000)
);

CREATE INDEX IF NOT EXISTS ix_jobs_status_created_at_utc ON jobs(status, created_at_utc);
