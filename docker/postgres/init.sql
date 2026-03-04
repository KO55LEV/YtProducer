CREATE TABLE IF NOT EXISTS playlists (
    id uuid PRIMARY KEY,
    title varchar(200) NOT NULL,
    description varchar(2000),
    status varchar(32) NOT NULL,
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL
);

CREATE TABLE IF NOT EXISTS tracks (
    id uuid PRIMARY KEY,
    playlist_id uuid NOT NULL REFERENCES playlists(id) ON DELETE CASCADE,
    title varchar(200) NOT NULL,
    source_url varchar(1000) NOT NULL,
    sort_order integer NOT NULL,
    duration interval,
    status varchar(32) NOT NULL,
    created_at_utc timestamptz NOT NULL,
    CONSTRAINT uq_tracks_playlist_sort_order UNIQUE (playlist_id, sort_order)
);

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
