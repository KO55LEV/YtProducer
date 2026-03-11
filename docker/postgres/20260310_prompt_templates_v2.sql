ALTER TABLE prompt_templates
    ADD COLUMN IF NOT EXISTS notes text,
    ADD COLUMN IF NOT EXISTS provider varchar(32) NOT NULL DEFAULT 'google',
    ADD COLUMN IF NOT EXISTS output_mode varchar(32) NOT NULL DEFAULT 'json',
    ADD COLUMN IF NOT EXISTS schema_key varchar(120),
    ADD COLUMN IF NOT EXISTS system_prompt text,
    ADD COLUMN IF NOT EXISTS user_prompt_template text,
    ADD COLUMN IF NOT EXISTS settings_json jsonb NOT NULL DEFAULT '{}'::jsonb,
    ADD COLUMN IF NOT EXISTS input_contract_json jsonb NOT NULL DEFAULT '{}'::jsonb,
    ADD COLUMN IF NOT EXISTS metadata_json jsonb NOT NULL DEFAULT '{}'::jsonb,
    ADD COLUMN IF NOT EXISTS is_default boolean NOT NULL DEFAULT false;

UPDATE prompt_templates
SET provider = COALESCE(NULLIF(provider, ''), 'google'),
    output_mode = COALESCE(NULLIF(output_mode, ''), 'json'),
    system_prompt = COALESCE(system_prompt, template_body),
    user_prompt_template = COALESCE(user_prompt_template, E'{\n  "theme": "{{theme}}"\n}')
WHERE system_prompt IS NULL
   OR user_prompt_template IS NULL
   OR provider IS NULL
   OR output_mode IS NULL;

CREATE INDEX IF NOT EXISTS ix_prompt_templates_provider ON prompt_templates(provider);
CREATE INDEX IF NOT EXISTS ix_prompt_templates_is_default ON prompt_templates(is_default);
CREATE INDEX IF NOT EXISTS ix_prompt_templates_category_provider ON prompt_templates(category, provider);
