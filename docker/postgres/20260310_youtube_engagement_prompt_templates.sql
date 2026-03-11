INSERT INTO prompt_templates (
    id,
    name,
    slug,
    category,
    description,
    notes,
    template_body,
    system_prompt,
    user_prompt_template,
    input_mode,
    provider,
    default_model,
    output_mode,
    schema_key,
    settings_json,
    input_contract_json,
    metadata_json,
    is_active,
    is_default,
    sort_order,
    version,
    created_at_utc,
    updated_at_utc
)
SELECT
    '9d8d79e2-7fd5-4a95-b4f0-a57165930f68'::uuid,
    'YouTube Pinned Comment Candidate',
    'youtube-pinned-comment-candidate',
    'youtube_engagement_comment',
    'Generate a short top-level engagement comment for a YouTube track upload.',
    'Purpose: create a concise pinned-comment candidate that feels native, references the track context, and asks one reply-driving question. Avoid spammy CTA language, hashtags, and long promo copy.',
    'Generate one short YouTube engagement comment using the provided track context.',
    E'You are writing a single top-level YouTube comment for a music upload.\n\nWrite like a real channel owner, not like an ad.\nKeep it short, natural, and specific to the track context.\nThe goal is to increase genuine replies.\n\nRules:\n- Output plain text only.\n- Maximum 280 characters.\n- 1 or 2 short sentences.\n- Include exactly one clear question.\n- No hashtags.\n- No quotation marks around the whole answer.\n- No markdown, labels, bullets, emojis-only replies, or JSON.\n- Avoid generic phrases like \"let me know what you think\", \"don''t forget to like and subscribe\", or \"drop a comment below\".\n- Avoid cringe hype and obvious bait.\n- Make the question feel tied to the workout/theme/track.\n- If the context is fitness-oriented, a lightly provocative training question is good, but keep it tasteful.',
    E'Track context:\n{{input_json}}\n\nWrite the best pinned-comment candidate for this upload.',
    'json_manual',
    'kie_ai',
    'gemini-2.5-pro',
    'text',
    NULL,
    '{"temperature":0.9}'::jsonb,
    '{
      "required": ["track_title", "youtube_title", "genre", "theme"],
      "optional": [
        "tempo_bpm",
        "key",
        "energy_level",
        "hook_type",
        "youtube_description",
        "playlist_title",
        "playlist_strategy",
        "target_audience",
        "listening_scenario",
        "channel_voice"
      ],
      "example": {
        "track_title": "Iron Grip",
        "youtube_title": "140 BPM Industrial Bass Workout Music | Bass Groove Intro",
        "genre": "Industrial Bass",
        "theme": "gym workout",
        "tempo_bpm": 140,
        "energy_level": 9,
        "hook_type": "bass groove intro",
        "playlist_title": "EXTREME ENERGY",
        "target_audience": "heavy lifters"
      }
    }'::jsonb,
    '{
      "provider":"kie_ai",
      "family":"gemini",
      "purpose":"youtube_engagement_comment",
      "engagement_type":"pinned_comment_candidate",
      "channel_surface":"youtube_video"
    }'::jsonb,
    true,
    false,
    310,
    1,
    NOW(),
    NOW()
WHERE NOT EXISTS (
    SELECT 1 FROM prompt_templates WHERE slug = 'youtube-pinned-comment-candidate'
);
