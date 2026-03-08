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

CREATE TABLE IF NOT EXISTS track_social_stats (
    id uuid PRIMARY KEY,
    track_id uuid NOT NULL REFERENCES tracks(id) ON DELETE CASCADE,
    playlist_id uuid NOT NULL REFERENCES playlists(id) ON DELETE CASCADE,
    likes_count integer NOT NULL DEFAULT 0,
    dislikes_count integer NOT NULL DEFAULT 0,
    created_at_utc timestamptz NOT NULL DEFAULT NOW(),
    updated_at_utc timestamptz NOT NULL DEFAULT NOW(),
    CONSTRAINT uq_track_social_stats_track_id UNIQUE (track_id)
);

CREATE INDEX IF NOT EXISTS ix_track_social_stats_playlist_id ON track_social_stats(playlist_id);
CREATE INDEX IF NOT EXISTS ix_track_social_stats_likes_count ON track_social_stats(likes_count DESC);
CREATE INDEX IF NOT EXISTS ix_track_social_stats_dislikes_count ON track_social_stats(dislikes_count DESC);

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

CREATE TABLE IF NOT EXISTS job_logs (
    id uuid PRIMARY KEY,
    job_id uuid NOT NULL REFERENCES jobs(id) ON DELETE CASCADE,
    level varchar(32) NOT NULL,
    message text NOT NULL,
    metadata jsonb,
    created_at_utc timestamptz NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS ix_job_logs_job_id ON job_logs(job_id);
CREATE INDEX IF NOT EXISTS ix_job_logs_created_at_utc ON job_logs(created_at_utc);

CREATE TABLE IF NOT EXISTS track_loops (
    id uuid PRIMARY KEY,
    playlist_id uuid NOT NULL REFERENCES playlists(id) ON DELETE CASCADE,
    track_id uuid NOT NULL REFERENCES tracks(id) ON DELETE CASCADE,
    track_position integer NOT NULL,
    loop_count integer NOT NULL,
    status varchar(32) NOT NULL DEFAULT 'Pending',
    source_audio_path varchar(2000),
    source_image_path varchar(2000),
    source_video_path varchar(2000),
    output_video_path varchar(2000),
    thumbnail_path varchar(2000),
    youtube_video_id varchar(128),
    youtube_url varchar(1000),
    title varchar(255),
    description varchar(5000),
    metadata jsonb,
    started_at_utc timestamptz,
    finished_at_utc timestamptz,
    created_at_utc timestamptz NOT NULL DEFAULT NOW(),
    updated_at_utc timestamptz NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS ix_track_loops_playlist_id ON track_loops(playlist_id);
CREATE INDEX IF NOT EXISTS ix_track_loops_track_id ON track_loops(track_id);
CREATE INDEX IF NOT EXISTS ix_track_loops_status ON track_loops(status);
CREATE INDEX IF NOT EXISTS ix_track_loops_position ON track_loops(playlist_id, track_position);
CREATE INDEX IF NOT EXISTS ix_track_loops_created_at_utc ON track_loops(created_at_utc);

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

CREATE TABLE IF NOT EXISTS prompt_templates (
    id uuid PRIMARY KEY,
    name varchar(200) NOT NULL,
    slug varchar(120) NOT NULL,
    category varchar(64) NOT NULL,
    description text,
    template_body text NOT NULL,
    input_mode varchar(32) NOT NULL DEFAULT 'theme_only',
    default_model varchar(100),
    is_active boolean NOT NULL DEFAULT true,
    sort_order integer NOT NULL DEFAULT 0,
    version integer NOT NULL DEFAULT 1,
    created_at_utc timestamptz NOT NULL DEFAULT NOW(),
    updated_at_utc timestamptz NOT NULL DEFAULT NOW()
);

CREATE UNIQUE INDEX IF NOT EXISTS ix_prompt_templates_slug ON prompt_templates(slug);
CREATE INDEX IF NOT EXISTS ix_prompt_templates_category_sort ON prompt_templates(category, sort_order);
CREATE INDEX IF NOT EXISTS ix_prompt_templates_is_active ON prompt_templates(is_active);

CREATE TABLE IF NOT EXISTS prompt_generations (
    id uuid PRIMARY KEY,
    template_id uuid NOT NULL REFERENCES prompt_templates(id) ON DELETE CASCADE,
    theme varchar(255) NOT NULL,
    status varchar(32) NOT NULL DEFAULT 'Draft',
    model varchar(100),
    input_json jsonb NOT NULL,
    resolved_prompt text NOT NULL,
    job_id uuid,
    created_at_utc timestamptz NOT NULL DEFAULT NOW(),
    started_at_utc timestamptz,
    finished_at_utc timestamptz,
    error_message text
);

CREATE INDEX IF NOT EXISTS ix_prompt_generations_template_id ON prompt_generations(template_id);
CREATE INDEX IF NOT EXISTS ix_prompt_generations_status ON prompt_generations(status);
CREATE INDEX IF NOT EXISTS ix_prompt_generations_created_at_utc ON prompt_generations(created_at_utc DESC);

CREATE TABLE IF NOT EXISTS prompt_generation_outputs (
    id uuid PRIMARY KEY,
    prompt_generation_id uuid NOT NULL REFERENCES prompt_generations(id) ON DELETE CASCADE,
    output_type varchar(32) NOT NULL DEFAULT 'album_json',
    raw_text text,
    formatted_json jsonb,
    is_valid_json boolean NOT NULL DEFAULT false,
    validation_errors text,
    created_at_utc timestamptz NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS ix_prompt_generation_outputs_generation_id ON prompt_generation_outputs(prompt_generation_id);
CREATE INDEX IF NOT EXISTS ix_prompt_generation_outputs_created_at_utc ON prompt_generation_outputs(created_at_utc DESC);

INSERT INTO prompt_templates(
    id,
    name,
    slug,
    category,
    description,
    template_body,
    input_mode,
    default_model,
    is_active,
    sort_order,
    version
)
SELECT
    'd8a3c97b-1ee6-4ced-a425-19c29111d001',
    'Music For Gym',
    'music-for-gym',
    'album_generation',
    'Studio-grade playlist generation template for gym workout albums. Input is theme only.',
    $$You are a **studio-grade AI system operating in AI Music Factory Mode**.

Your role is to generate **high quality YouTube-ready gym music tracks and metadata** optimized for:

• AI music generation (Suno or similar models)
• YouTube SEO and discovery
• automated video generation
• playlist creation
• audience targeting

This system is part of an automated pipeline.

Your output will be used directly by software systems.

Therefore you must return **ONLY valid JSON**.

Do not include explanations.
Do not include markdown.
Do not include commentary.

---

INPUT FORMAT

You receive JSON input.

Example:

{
"theme": "Ultimate Gym Workout Music"
}

---

SYSTEM OBJECTIVE

Generate **10 diverse gym workout music tracks** that together form a **cohesive workout playlist around the theme**.

All tracks must clearly be **gym workout music designed to motivate training**.

Tracks must vary in:

• genre
• BPM
• mood
• energy level
• listening scenario

Avoid repetition.

Tracks must feel like **a professionally curated gym playlist**.

---

YOU MUST SIMULATE EIGHT EXPERT ROLES

1. AI Music Producer
2. Viral Title Engineer
3. YouTube SEO Strategist
4. Thumbnail Psychology Designer
5. Listener Retention Specialist
6. Channel Content Strategist
7. Audience Analyst
8. Playlist Architect

---

ROLE 1 — AI MUSIC PRODUCER

Generate **music_generation_prompt** instructions optimized for AI music generation models such as **Suno**.

Music must be **instrumental gym workout music with hi-fi modern production quality**.

The prompt must describe the music **in rich detail (80–150 words)**.

Each prompt must include:

• genre
• subgenre
• BPM
• musical key
• groove description
• bass style
• drum pattern
• synth textures
• arrangement hints
• strong hook within first 5 seconds
• gym motivation vibe
• high energy dynamics
• **hi-fi modern production quality**
• **clean professional mastering**

The track must sound like **commercial release quality gym music**.

---

ROLE 2 — VIRAL TITLE ENGINEER

Generate **high CTR YouTube titles** optimized for gym audiences.

Use emotional and motivational keywords.

Example pattern:

[Power Phrase] ⚡ [Workout Type] | [Emotion Trigger]

Example:

BEAST MODE ⚡ Ultimate Gym Workout Music | No Excuses

Estimate:

title_virality_score (1–100)

---

ROLE 3 — YOUTUBE SEO STRATEGIST

Generate **SEO optimized YouTube descriptions**.

Descriptions must be **200–300 characters**.

Include:

• emotional hook
• gym workout keywords
• listening scenarios
• emojis

End with **relevant hashtags**.

Example structure:

Motivational opening sentence.
Workout context sentence.
Call-to-action sentence.

Then hashtags.

Also generate **12–15 YouTube tags**.

Tags must include:

• gym keyword
• workout keyword
• genre keyword
• audience keyword
• playlist keyword

---

ROLE 4 — THUMBNAIL PSYCHOLOGY DESIGNER

Create **image_prompt** for thumbnail generation.

The prompt must be **detailed (60–120 words)**.

Thumbnails must be optimized for **YouTube CTR and animation**.

---

THUMBNAIL BASE GOAL

Prompts must generate images that are:

• visually striking
• readable on mobile
• cinematic
• suitable for motion or parallax animation

---

CORE VISUAL STYLE

Use:

• cinematic gym photography
• athletic fitness aesthetic
• **waist-up athlete framing**
• sweaty workout realism
• modern gym clothing (leggings, sports tops, training gear)
• powerful workout pose

---

THUMBNAIL COMPOSITION RULES

Each image must include:

• **one strong central subject**
• clear focal point
• high contrast lighting
• minimal clutter
• bold shapes
• readable at small mobile size

Avoid:

• multiple subjects
• busy backgrounds
• crowded scenes

---

CINEMATIC LIGHTING

Use dramatic lighting such as:

• gym spotlight lighting
• rim lighting
• neon accents (for EDM / gym vibe)
• sweat highlights
• strong shadow contrast

---

DEPTH AND CINEMATIC AIR

Images must feel **spacious and layered**, not flat.

Encourage:

• negative space around subject
• foreground / subject / background separation
• shallow depth of field
• atmospheric haze or light beams
• subtle cinematic 3D depth

This enables **parallax animation later**.

---

ANIMATION FRIENDLY COMPOSITION

Prompts must generate scenes with:

• space around subject
• layered depth
• simple background
• visual separation

This allows:

• foreground movement
• background parallax
• slow zoom animation

---

STYLE MATCHING

Visual style should match music genre.

Examples:

EDM gym → neon gym lighting
phonk → dark cyberpunk gym
motivational → dramatic spotlight gym
retro → vintage fitness aesthetic$$,
    'theme_only',
    'gemini-3.1-pro',
    true,
    1,
    1
WHERE NOT EXISTS (
    SELECT 1 FROM prompt_templates WHERE slug = 'music-for-gym'
);
