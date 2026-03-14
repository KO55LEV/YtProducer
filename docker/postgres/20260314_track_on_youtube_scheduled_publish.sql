alter table if exists track_on_youtube
    add column if not exists scheduled_publish_at_utc timestamptz;
