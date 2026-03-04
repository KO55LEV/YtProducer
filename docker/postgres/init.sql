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
    type varchar(32) NOT NULL,
    status varchar(32) NOT NULL DEFAULT 'Pending',
    target_type varchar(32),
    target_id uuid,
    job_group_id uuid,
    sequence integer,
    progress integer NOT NULL DEFAULT 0,
    payload_json jsonb,
    result_json jsonb,
    retry_count integer NOT NULL DEFAULT 0,
    max_retries integer NOT NULL DEFAULT 3,
    worker_id varchar(256),
    lease_expires_at timestamptz,
    last_heartbeat timestamptz,
    created_at timestamptz NOT NULL DEFAULT NOW(),
    started_at timestamptz,
    finished_at timestamptz,
    error_code varchar(64),
    error_message text,
    idempotency_key varchar(128)
);

CREATE INDEX IF NOT EXISTS idx_jobs_poll ON jobs(status, created_at);
CREATE INDEX IF NOT EXISTS idx_jobs_target ON jobs(target_type, target_id);
CREATE INDEX IF NOT EXISTS idx_jobs_lease ON jobs(lease_expires_at);
CREATE UNIQUE INDEX IF NOT EXISTS idx_jobs_idempotency ON jobs(idempotency_key) WHERE idempotency_key IS NOT NULL;
