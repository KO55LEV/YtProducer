CREATE TABLE IF NOT EXISTS playlists (
    id uuid PRIMARY KEY,
    title varchar(255) NOT NULL,
    theme varchar(200),
    description varchar(2000),
    playlist_strategy text,
    status varchar(32) NOT NULL DEFAULT 'Draft',
    track_count integer DEFAULT 0,
    youtube_playlist_id varchar(128),
    metadata jsonb,
    created_at_utc timestamptz NOT NULL DEFAULT NOW(),
    updated_at_utc timestamptz NOT NULL DEFAULT NOW(),
    published_at_utc timestamptz
);

CREATE INDEX IF NOT EXISTS ix_playlists_status ON playlists(status);
CREATE INDEX IF NOT EXISTS ix_playlists_theme ON playlists(theme);
CREATE INDEX IF NOT EXISTS ix_playlists_youtube_playlist_id ON playlists(youtube_playlist_id);
CREATE INDEX IF NOT EXISTS ix_playlists_created_at ON playlists(created_at_utc DESC);
CREATE INDEX IF NOT EXISTS ix_playlists_metadata ON playlists USING gin(metadata);

CREATE TABLE IF NOT EXISTS youtube_playlists (
    id uuid PRIMARY KEY,
    playlist_id uuid NOT NULL REFERENCES playlists(id) ON DELETE CASCADE,
    youtube_playlist_id varchar(128) NOT NULL,
    title varchar(255),
    description varchar(5000),
    status varchar(32),
    privacy_status varchar(32),
    channel_id varchar(128),
    channel_title varchar(255),
    item_count integer,
    published_at_utc timestamptz,
    thumbnail_url varchar(1000),
    etag varchar(128),
    last_synced_at_utc timestamptz,
    metadata jsonb,
    created_at_utc timestamptz NOT NULL DEFAULT NOW(),
    updated_at_utc timestamptz NOT NULL DEFAULT NOW()
);

CREATE UNIQUE INDEX IF NOT EXISTS ix_youtube_playlists_youtube_id ON youtube_playlists(youtube_playlist_id);
CREATE INDEX IF NOT EXISTS ix_youtube_playlists_playlist_id ON youtube_playlists(playlist_id);
CREATE INDEX IF NOT EXISTS ix_youtube_playlists_status ON youtube_playlists(status);
CREATE INDEX IF NOT EXISTS ix_youtube_playlists_metadata ON youtube_playlists USING gin(metadata);

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

CREATE TABLE IF NOT EXISTS track_images (
    id uuid PRIMARY KEY,
    track_id uuid NOT NULL REFERENCES tracks(id) ON DELETE CASCADE,
    playlist_id uuid NOT NULL REFERENCES playlists(id) ON DELETE CASCADE,
    playlist_position integer NOT NULL,
    file_name varchar(256) NOT NULL,
    file_path varchar(2000) NOT NULL,
    source_url varchar(2000),
    model varchar(100),
    prompt text,
    aspect_ratio varchar(32),
    created_at_utc timestamptz NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS ix_track_images_track_id ON track_images(track_id);
CREATE INDEX IF NOT EXISTS ix_track_images_playlist_id ON track_images(playlist_id);
CREATE INDEX IF NOT EXISTS ix_track_images_playlist_position ON track_images(playlist_id, playlist_position);

CREATE TABLE IF NOT EXISTS track_on_youtube (
    id uuid PRIMARY KEY,
    track_id uuid NOT NULL REFERENCES tracks(id) ON DELETE CASCADE,
    playlist_id uuid NOT NULL REFERENCES playlists(id) ON DELETE CASCADE,
    playlist_position integer NOT NULL,
    video_id varchar(64) NOT NULL,
    url varchar(2000),
    title varchar(200),
    description text,
    privacy varchar(32),
    file_path varchar(2000),
    status varchar(32),
    metadata jsonb,
    created_at_utc timestamptz NOT NULL DEFAULT NOW()
);

CREATE UNIQUE INDEX IF NOT EXISTS ix_track_on_youtube_video_id ON track_on_youtube(video_id);
CREATE INDEX IF NOT EXISTS ix_track_on_youtube_track_id ON track_on_youtube(track_id);
CREATE INDEX IF NOT EXISTS ix_track_on_youtube_playlist_id ON track_on_youtube(playlist_id);
CREATE INDEX IF NOT EXISTS ix_track_on_youtube_playlist_position ON track_on_youtube(playlist_id, playlist_position);

CREATE TABLE IF NOT EXISTS track_video_generation (
    id uuid PRIMARY KEY,
    track_id uuid NOT NULL REFERENCES tracks(id) ON DELETE CASCADE,
    playlist_id uuid NOT NULL REFERENCES playlists(id) ON DELETE CASCADE,
    playlist_position integer NOT NULL,
    status varchar(32) NOT NULL DEFAULT 'Pending',
    progress_percent integer NOT NULL DEFAULT 0,
    progress_current_frame integer,
    progress_total_frames integer,
    track_duration_seconds double precision,
    image_path varchar(2000),
    audio_path varchar(2000),
    temp_dir varchar(2000),
    output_dir varchar(2000),
    width integer,
    height integer,
    fps integer,
    eq_bands integer,
    video_bitrate varchar(32),
    audio_bitrate varchar(32),
    seed integer,
    use_gpu boolean,
    keep_temp boolean,
    use_raw_pipe boolean,
    renderer_variant varchar(32),
    output_file_name_override varchar(256),
    logo_path varchar(2000),
    output_video_path varchar(2000),
    analysis_path varchar(2000),
    ffmpeg_command text,
    error_message text,
    metadata jsonb,
    started_at_utc timestamptz,
    finished_at_utc timestamptz,
    created_at_utc timestamptz NOT NULL DEFAULT NOW(),
    updated_at_utc timestamptz NOT NULL DEFAULT NOW(),
    CONSTRAINT uq_track_video_generation_track_id UNIQUE (track_id)
);

CREATE INDEX IF NOT EXISTS ix_track_video_generation_playlist_id ON track_video_generation(playlist_id);
CREATE INDEX IF NOT EXISTS ix_track_video_generation_playlist_position ON track_video_generation(playlist_id, playlist_position);
CREATE INDEX IF NOT EXISTS ix_track_video_generation_status ON track_video_generation(status);
CREATE INDEX IF NOT EXISTS ix_track_video_generation_updated_at ON track_video_generation(updated_at_utc DESC);
CREATE INDEX IF NOT EXISTS ix_track_video_generation_metadata ON track_video_generation USING gin(metadata);

CREATE TABLE IF NOT EXISTS youtube_last_published_date (
    id integer PRIMARY KEY CHECK (id = 1),
    last_published_date timestamptz NOT NULL,
    video_id varchar(64)
);

INSERT INTO youtube_last_published_date(id, last_published_date, video_id)
VALUES (1, '2026-03-08T08:00:00+00:00', NULL)
ON CONFLICT (id) DO NOTHING;

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

CREATE TABLE IF NOT EXISTS youtube_upload_queue (
    id uuid PRIMARY KEY,
    status varchar(32) NOT NULL DEFAULT 'Pending',
    priority integer NOT NULL DEFAULT 0,
    title varchar(255) NOT NULL,
    description varchar(5000),
    tags text[],
    category_id integer NOT NULL DEFAULT 10,
    video_file_path varchar(1000) NOT NULL,
    thumbnail_file_path varchar(1000),
    youtube_video_id varchar(128),
    youtube_url varchar(500),
    scheduled_upload_at timestamptz,
    attempts integer NOT NULL DEFAULT 0,
    max_attempts integer NOT NULL DEFAULT 5,
    last_error text,
    created_at timestamptz NOT NULL DEFAULT NOW(),
    updated_at timestamptz NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS ix_youtube_upload_queue_status ON youtube_upload_queue(status);
CREATE INDEX IF NOT EXISTS ix_youtube_upload_queue_scheduled_upload_at ON youtube_upload_queue(scheduled_upload_at);
CREATE INDEX IF NOT EXISTS ix_youtube_upload_queue_priority ON youtube_upload_queue(priority);
CREATE INDEX IF NOT EXISTS ix_youtube_upload_queue_composite ON youtube_upload_queue(status, scheduled_upload_at, priority);
