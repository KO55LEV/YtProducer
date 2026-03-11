DROP TABLE IF EXISTS prompt_generation_outputs;
DROP TABLE IF EXISTS prompt_generations;

CREATE TABLE prompt_generations (
    id uuid PRIMARY KEY,
    template_id uuid NOT NULL REFERENCES prompt_templates(id) ON DELETE CASCADE,
    purpose varchar(64) NOT NULL,
    provider varchar(32) NOT NULL,
    model varchar(100),
    input_label varchar(255),
    input_json jsonb NOT NULL,
    resolved_system_prompt text NOT NULL,
    resolved_user_prompt text NOT NULL,
    status varchar(32) NOT NULL DEFAULT 'Draft',
    error_message text,
    latency_ms integer,
    token_usage_json jsonb NOT NULL DEFAULT '{}'::jsonb,
    run_metadata_json jsonb NOT NULL DEFAULT '{}'::jsonb,
    target_type varchar(64),
    target_id varchar(255),
    job_id uuid,
    created_at_utc timestamptz NOT NULL DEFAULT NOW(),
    started_at_utc timestamptz,
    finished_at_utc timestamptz
);

CREATE INDEX ix_prompt_generations_template_id ON prompt_generations(template_id);
CREATE INDEX ix_prompt_generations_status ON prompt_generations(status);
CREATE INDEX ix_prompt_generations_created_at_utc ON prompt_generations(created_at_utc DESC);
CREATE INDEX ix_prompt_generations_purpose ON prompt_generations(purpose);
CREATE INDEX ix_prompt_generations_provider ON prompt_generations(provider);
CREATE INDEX ix_prompt_generations_target ON prompt_generations(target_type, target_id);

CREATE TABLE prompt_generation_outputs (
    id uuid PRIMARY KEY,
    prompt_generation_id uuid NOT NULL REFERENCES prompt_generations(id) ON DELETE CASCADE,
    output_type varchar(64) NOT NULL DEFAULT 'text',
    output_label varchar(255),
    output_text text,
    output_json jsonb,
    is_primary boolean NOT NULL DEFAULT false,
    is_valid boolean NOT NULL DEFAULT false,
    validation_errors text,
    provider_response_json jsonb,
    created_at_utc timestamptz NOT NULL DEFAULT NOW()
);

CREATE INDEX ix_prompt_generation_outputs_generation_id ON prompt_generation_outputs(prompt_generation_id);
CREATE INDEX ix_prompt_generation_outputs_created_at_utc ON prompt_generation_outputs(created_at_utc DESC);
CREATE INDEX ix_prompt_generation_outputs_is_primary ON prompt_generation_outputs(is_primary);
