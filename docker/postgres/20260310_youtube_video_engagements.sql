create table if not exists youtube_video_engagements
(
    id uuid primary key,
    channel_id text not null,
    youtube_video_id text not null,
    track_id uuid null,
    playlist_id uuid null,
    album_release_id uuid null,
    engagement_type text not null,
    prompt_template_id uuid null,
    prompt_generation_id uuid null,
    provider text null,
    model text null,
    generated_text text null,
    final_text text null,
    status text not null default 'Draft',
    youtube_comment_id text null,
    posted_at_utc timestamptz null,
    error_message text null,
    metadata_json jsonb not null default '{}'::jsonb,
    created_at_utc timestamptz not null default now(),
    updated_at_utc timestamptz not null default now()
);

create index if not exists ix_youtube_video_engagements_channel_id
    on youtube_video_engagements(channel_id);

create index if not exists ix_youtube_video_engagements_youtube_video_id
    on youtube_video_engagements(youtube_video_id);

create index if not exists ix_youtube_video_engagements_track_id
    on youtube_video_engagements(track_id);

create index if not exists ix_youtube_video_engagements_playlist_id
    on youtube_video_engagements(playlist_id);

create index if not exists ix_youtube_video_engagements_album_release_id
    on youtube_video_engagements(album_release_id);

create index if not exists ix_youtube_video_engagements_status
    on youtube_video_engagements(status);

create index if not exists ix_youtube_video_engagements_prompt_generation_id
    on youtube_video_engagements(prompt_generation_id);

create index if not exists ix_youtube_video_engagements_channel_video
    on youtube_video_engagements(channel_id, youtube_video_id);
