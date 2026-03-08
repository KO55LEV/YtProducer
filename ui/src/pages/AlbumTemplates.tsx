import { useEffect, useMemo, useState } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import type { PromptGeneration, PromptTemplate } from "../types";

const apiBaseUrl = import.meta.env.VITE_API_BASE_URL ?? "http://localhost:8080";

const emptyTemplateForm = {
  name: "",
  slug: "",
  category: "album_generation",
  description: "",
  templateBody: "",
  inputMode: "theme_only",
  defaultModel: "gemini-3.1-pro",
  isActive: true,
  sortOrder: 100
};

function formatDate(value?: string | null): string {
  if (!value) return "—";
  return new Date(value).toLocaleString();
}

function generationTone(status: string): string {
  switch (status.toLowerCase()) {
    case "completed":
      return "success";
    case "failed":
      return "error";
    case "running":
      return "primary";
    default:
      return "secondary";
  }
}

function buildResolvedPromptPreview(templateBody: string, theme: string): string {
  const effectiveTheme = theme || "Ultimate Gym Workout Music";
  const inputJson = JSON.stringify({ theme: effectiveTheme }, null, 2);
  return `[SYSTEM PROMPT]
${templateBody}

[USER INPUT JSON]
${inputJson}`;
}

function countValidOutputs(generations: PromptGeneration[]): number {
  return generations.reduce((total, generation) => {
    return total + generation.outputs.filter((output) => output.isValidJson).length;
  }, 0);
}

export default function AlbumTemplates() {
  const navigate = useNavigate();
  const { id } = useParams<{ id?: string }>();
  const isNewTemplate = id === "new";
  const isListView = !id;

  const [templates, setTemplates] = useState<PromptTemplate[]>([]);
  const [generations, setGenerations] = useState<PromptGeneration[]>([]);
  const [selectedGenerationId, setSelectedGenerationId] = useState<string | null>(null);
  const [templateForm, setTemplateForm] = useState(emptyTemplateForm);
  const [theme, setTheme] = useState("");
  const [outputDraft, setOutputDraft] = useState("");
  const [loading, setLoading] = useState(true);
  const [generationsLoading, setGenerationsLoading] = useState(false);
  const [savingTemplate, setSavingTemplate] = useState(false);
  const [creatingGeneration, setCreatingGeneration] = useState(false);
  const [savingOutput, setSavingOutput] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const selectedTemplate = useMemo(() => {
    if (!id || isNewTemplate) {
      return null;
    }

    return templates.find((item) => item.id === id) ?? null;
  }, [templates, id, isNewTemplate]);

  const selectedGeneration = useMemo(
    () => generations.find((item) => item.id === selectedGenerationId) ?? null,
    [generations, selectedGenerationId]
  );

  const latestOutput = selectedGeneration?.outputs[0] ?? null;

  useEffect(() => {
    void loadTemplates();
  }, []);

  useEffect(() => {
    if (isListView) {
      return;
    }

    if (isNewTemplate) {
      setTemplateForm(emptyTemplateForm);
      setGenerations([]);
      setSelectedGenerationId(null);
      setTheme("");
      setOutputDraft("");
      return;
    }

    if (!selectedTemplate) {
      return;
    }

    setTemplateForm({
      name: selectedTemplate.name,
      slug: selectedTemplate.slug,
      category: selectedTemplate.category,
      description: selectedTemplate.description ?? "",
      templateBody: selectedTemplate.templateBody,
      inputMode: selectedTemplate.inputMode,
      defaultModel: selectedTemplate.defaultModel ?? "",
      isActive: selectedTemplate.isActive,
      sortOrder: selectedTemplate.sortOrder
    });

    setTheme("");
    setOutputDraft("");
    void loadGenerations(selectedTemplate.id);
  }, [selectedTemplate, isNewTemplate, isListView]);

  async function loadTemplates(): Promise<void> {
    try {
      setLoading(true);
      const response = await fetch(`${apiBaseUrl}/prompt-templates`);
      if (!response.ok) {
        throw new Error(`Templates request failed with status ${response.status}`);
      }

      const data = (await response.json()) as PromptTemplate[];
      setTemplates(data);
      setError(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to load templates");
    } finally {
      setLoading(false);
    }
  }

  async function loadGenerations(templateId: string): Promise<void> {
    try {
      setGenerationsLoading(true);
      const response = await fetch(`${apiBaseUrl}/prompt-templates/${templateId}/generations`);
      if (!response.ok) {
        throw new Error(`Generations request failed with status ${response.status}`);
      }

      const data = (await response.json()) as PromptGeneration[];
      setGenerations(data);
      setSelectedGenerationId((current) => {
        if (current && data.some((item) => item.id === current)) {
          return current;
        }

        return data[0]?.id ?? null;
      });
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to load generations");
    } finally {
      setGenerationsLoading(false);
    }
  }

  async function handleSaveTemplate(): Promise<void> {
    try {
      setSavingTemplate(true);
      setError(null);

      const payload = {
        ...templateForm,
        defaultModel: templateForm.defaultModel || null
      };

      const response = await fetch(
        selectedTemplate ? `${apiBaseUrl}/prompt-templates/${selectedTemplate.id}` : `${apiBaseUrl}/prompt-templates`,
        {
          method: selectedTemplate ? "PUT" : "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify(payload)
        }
      );

      if (!response.ok) {
        throw new Error(`Save template failed with status ${response.status}`);
      }

      const saved = (await response.json()) as PromptTemplate;
      await loadTemplates();
      navigate(`/album-templates/${saved.id}`);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to save template");
    } finally {
      setSavingTemplate(false);
    }
  }

  async function handleCreateGeneration(): Promise<void> {
    if (!selectedTemplate || !theme.trim()) return;

    try {
      setCreatingGeneration(true);
      setError(null);

      const response = await fetch(`${apiBaseUrl}/prompt-templates/${selectedTemplate.id}/generations`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          theme: theme.trim(),
          model: templateForm.defaultModel || null
        })
      });

      if (!response.ok) {
        throw new Error(`Create generation failed with status ${response.status}`);
      }

      const created = (await response.json()) as PromptGeneration;
      await loadGenerations(selectedTemplate.id);
      setSelectedGenerationId(created.id);
      setOutputDraft("");
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to create generation");
    } finally {
      setCreatingGeneration(false);
    }
  }

  async function handleSaveOutput(): Promise<void> {
    if (!selectedGeneration || !outputDraft.trim()) return;

    try {
      setSavingOutput(true);
      setError(null);

      const response = await fetch(`${apiBaseUrl}/prompt-generations/${selectedGeneration.id}/outputs`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          rawText: outputDraft.trim(),
          outputType: "album_json"
        })
      });

      if (!response.ok) {
        throw new Error(`Save output failed with status ${response.status}`);
      }

      const updated = (await response.json()) as PromptGeneration;
      setGenerations((current) => current.map((item) => (item.id === updated.id ? updated : item)));
      setSelectedGenerationId(updated.id);
      setOutputDraft("");
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to save output");
    } finally {
      setSavingOutput(false);
    }
  }

  if (isListView) {
    return (
      <div className="page-content">
        <div className="page-header-section album-template-index-header">
          <div>
            <h1 className="page-title">Album Templates</h1>
            <p className="page-subtitle">Choose a prompt style first. Editing, generation, and output review happen inside the template.</p>
          </div>
          <div className="header-actions">
            <Link to="/album-templates/new" className="btn btn-primary">
              New Template
            </Link>
          </div>
        </div>

        {error && (
          <div className="alert alert-error">
            <strong>Error:</strong> {error}
          </div>
        )}

        {loading ? (
          <div className="loading-state">
            <div className="spinner"></div>
            <p>Loading templates...</p>
          </div>
        ) : (
          <div className="template-gallery">
            {templates.map((template) => (
              <button
                key={template.id}
                type="button"
                className="template-gallery-card"
                onClick={() => navigate(`/album-templates/${template.id}`)}
              >
                <div className="template-gallery-top">
                  <div>
                    <h2>{template.name}</h2>
                    <p className="template-gallery-slug">{template.slug}</p>
                  </div>
                  <span className={`badge badge-${template.isActive ? "success" : "secondary"}`}>
                    {template.isActive ? "Active" : "Inactive"}
                  </span>
                </div>
                <p className="template-gallery-description">{template.description || "No description provided."}</p>
                <div className="template-gallery-meta">
                  <span>{template.category}</span>
                  <span>{template.defaultModel ?? "No model"}</span>
                  <span>v{template.version}</span>
                </div>
                <div className="template-gallery-footer">
                  <span>{template.inputMode.replace("_", " ")}</span>
                  <span className="template-gallery-link">Open</span>
                </div>
              </button>
            ))}
          </div>
        )}
      </div>
    );
  }

  if (!isNewTemplate && !loading && !selectedTemplate) {
    return (
      <div className="page-content">
        <div className="empty-state">
          <h2>Template not found</h2>
          <p>The selected template could not be loaded.</p>
          <Link to="/album-templates" className="btn btn-secondary">
            Back to Templates
          </Link>
        </div>
      </div>
    );
  }

  const templateName = selectedTemplate?.name ?? "New Template";
  const templateOutputCount = countValidOutputs(generations);

  return (
    <div className="page-content">
      <div className="page-header-section album-template-detail-header">
        <div>
          <Link to="/album-templates" className="breadcrumb">
            ← Back to Templates
          </Link>
          <h1 className="page-title">{templateName}</h1>
          <p className="page-subtitle">
            {selectedTemplate
              ? "Update the reusable prompt, create themed generations, and inspect returned JSON."
              : "Create a reusable prompt template for a new album style."}
          </p>
        </div>
        <div className="header-actions">
          <button type="button" className="btn btn-primary" onClick={handleSaveTemplate} disabled={savingTemplate}>
            {savingTemplate ? "Saving..." : "Save Template"}
          </button>
        </div>
      </div>

      {error && (
        <div className="alert alert-error">
          <strong>Error:</strong> {error}
        </div>
      )}

      <div className="album-template-detail-layout">
        <section className="album-template-main">
          <section className="prompt-card-shell">
            <div className="prompt-card-header">
              <div>
                <h2 className="section-title">Template Editor</h2>
                <p className="prompt-card-subtitle">This defines the reusable album-generation style.</p>
              </div>
            </div>

            <div className="prompt-form-grid">
              <label className="prompt-field">
                <span>Name</span>
                <input value={templateForm.name} onChange={(e) => setTemplateForm((c) => ({ ...c, name: e.target.value }))} />
              </label>
              <label className="prompt-field">
                <span>Slug</span>
                <input value={templateForm.slug} onChange={(e) => setTemplateForm((c) => ({ ...c, slug: e.target.value }))} />
              </label>
              <label className="prompt-field">
                <span>Category</span>
                <input value={templateForm.category} onChange={(e) => setTemplateForm((c) => ({ ...c, category: e.target.value }))} />
              </label>
              <label className="prompt-field">
                <span>Default Model</span>
                <input value={templateForm.defaultModel} onChange={(e) => setTemplateForm((c) => ({ ...c, defaultModel: e.target.value }))} />
              </label>
              <label className="prompt-field">
                <span>Input Mode</span>
                <input value={templateForm.inputMode} onChange={(e) => setTemplateForm((c) => ({ ...c, inputMode: e.target.value }))} />
              </label>
              <label className="prompt-field">
                <span>Sort Order</span>
                <input
                  type="number"
                  value={templateForm.sortOrder}
                  onChange={(e) => setTemplateForm((c) => ({ ...c, sortOrder: Number.parseInt(e.target.value || "0", 10) || 0 }))}
                />
              </label>
              <label className="prompt-field prompt-field-full">
                <span>Description</span>
                <input value={templateForm.description} onChange={(e) => setTemplateForm((c) => ({ ...c, description: e.target.value }))} />
              </label>
              <label className="prompt-field prompt-field-full">
                <span>Template Body</span>
                <textarea
                  rows={20}
                  value={templateForm.templateBody}
                  onChange={(e) => setTemplateForm((c) => ({ ...c, templateBody: e.target.value }))}
                />
              </label>
            </div>
          </section>

          <section className="prompt-card-shell">
            <div className="prompt-card-header">
              <div>
                <h2 className="section-title">Generate</h2>
                <p className="prompt-card-subtitle">Runtime input stays simple. You only provide the theme.</p>
              </div>
              <button
                type="button"
                className="btn btn-primary"
                onClick={handleCreateGeneration}
                disabled={!selectedTemplate || !theme.trim() || creatingGeneration}
              >
                {creatingGeneration ? "Creating..." : "Create Generation"}
              </button>
            </div>

            <div className="album-template-generate-grid">
              <div className="prompt-generation-form">
                <label className="prompt-field">
                  <span>Theme</span>
                  <input placeholder="Ultimate Gym Workout Music" value={theme} onChange={(e) => setTheme(e.target.value)} />
                </label>

                <label className="prompt-field">
                  <span>Input JSON Preview</span>
                  <pre className="prompt-code-block">{JSON.stringify({ theme: theme || "Ultimate Gym Workout Music" }, null, 2)}</pre>
                </label>
              </div>

              <label className="prompt-field">
                <span>Gemini Request Preview</span>
                <pre className="prompt-code-block prompt-code-block-tall">
                  {buildResolvedPromptPreview(templateForm.templateBody, theme)}
                </pre>
              </label>
            </div>
          </section>

          {selectedGeneration && (
            <section className="prompt-card-shell template-output-section">
              <div className="prompt-card-header">
                <div>
                  <h2 className="section-title">Generation Output</h2>
                  <p className="prompt-card-subtitle">Paste returned JSON, validate it, and keep the saved formatted result.</p>
                </div>
                <button
                  type="button"
                  className="btn btn-primary"
                  onClick={handleSaveOutput}
                  disabled={!outputDraft.trim() || savingOutput}
                >
                  {savingOutput ? "Saving..." : "Save Output"}
                </button>
              </div>

              <div className="prompt-output-layout">
                <div className="prompt-output-summary">
                  <div className="prompt-output-summary-grid">
                    <div>
                      <span className="job-summary-label">Theme</span>
                      <span className="job-summary-value">{selectedGeneration.theme}</span>
                    </div>
                    <div>
                      <span className="job-summary-label">Status</span>
                      <span className="job-summary-value">{selectedGeneration.status}</span>
                    </div>
                    <div>
                      <span className="job-summary-label">Model</span>
                      <span className="job-summary-value">{selectedGeneration.model ?? "—"}</span>
                    </div>
                    <div>
                      <span className="job-summary-label">Created</span>
                      <span className="job-summary-value">{formatDate(selectedGeneration.createdAtUtc)}</span>
                    </div>
                  </div>
                </div>

                <div className="template-output-grid">
                  <label className="prompt-field">
                    <span>Returned JSON / Raw Response</span>
                    <textarea
                      rows={14}
                      value={outputDraft}
                      onChange={(e) => setOutputDraft(e.target.value)}
                      placeholder="Paste model JSON response here..."
                    />
                  </label>

                  <label className="prompt-field">
                    <span>Formatted JSON</span>
                    <pre className="prompt-code-block prompt-code-block-output">
                      {latestOutput?.formattedJson ?? latestOutput?.rawText ?? "No saved output yet."}
                    </pre>
                  </label>
                </div>

                {latestOutput && !latestOutput.isValidJson && latestOutput.validationErrors && (
                  <div className="alert alert-error compact-alert">
                    <strong>Validation:</strong> {latestOutput.validationErrors}
                  </div>
                )}

                <label className="prompt-field">
                  <span>Saved Gemini Request</span>
                  <pre className="prompt-code-block prompt-code-block-medium">{selectedGeneration.resolvedPrompt}</pre>
                </label>
              </div>
            </section>
          )}
        </section>

        <aside className="album-template-sidebar">
          <section className="prompt-card-shell album-template-summary-card">
            <div className="prompt-card-header">
              <div>
                <h2 className="section-title">Template Summary</h2>
                <p className="prompt-card-subtitle">Quick state and usage snapshot.</p>
              </div>
            </div>

            <div className="album-template-summary-list">
              <div className="album-template-summary-item">
                <span className="job-summary-label">Status</span>
                <span className={`badge badge-${templateForm.isActive ? "success" : "secondary"}`}>
                  {templateForm.isActive ? "Active" : "Inactive"}
                </span>
              </div>
              <div className="album-template-summary-item">
                <span className="job-summary-label">Category</span>
                <span className="job-summary-value">{templateForm.category || "—"}</span>
              </div>
              <div className="album-template-summary-item">
                <span className="job-summary-label">Default Model</span>
                <span className="job-summary-value">{templateForm.defaultModel || "—"}</span>
              </div>
              <div className="album-template-summary-item">
                <span className="job-summary-label">Slug</span>
                <span className="job-summary-value">{templateForm.slug || "—"}</span>
              </div>
              <div className="album-template-summary-item">
                <span className="job-summary-label">Generations</span>
                <span className="job-summary-value">{generations.length}</span>
              </div>
              <div className="album-template-summary-item">
                <span className="job-summary-label">Valid Outputs</span>
                <span className="job-summary-value">{templateOutputCount}</span>
              </div>
            </div>
          </section>

          <section className="prompt-card-shell">
            <div className="prompt-card-header">
              <div>
                <h2 className="section-title">Generations</h2>
                <p className="prompt-card-subtitle">Stored runs for this template.</p>
              </div>
            </div>

            {generationsLoading ? (
              <div className="loading-state compact-loading">
                <div className="spinner"></div>
                <p>Loading generations...</p>
              </div>
            ) : generations.length === 0 ? (
              <div className="empty-state compact-empty">
                <h3>No generations yet</h3>
                <p>Create a themed run to start collecting JSON outputs.</p>
              </div>
            ) : (
              <div className="prompt-generation-list">
                {generations.map((generation) => (
                  <button
                    key={generation.id}
                    type="button"
                    className={`prompt-generation-item ${selectedGenerationId === generation.id ? "is-active" : ""}`}
                    onClick={() => setSelectedGenerationId(generation.id)}
                  >
                    <div className="prompt-generation-item-top">
                      <span className="prompt-generation-theme">{generation.theme}</span>
                      <span className={`badge badge-${generationTone(generation.status)}`}>{generation.status}</span>
                    </div>
                    <div className="prompt-generation-item-meta">
                      <span>{generation.model ?? "—"}</span>
                      <span>{formatDate(generation.createdAtUtc)}</span>
                    </div>
                  </button>
                ))}
              </div>
            )}
          </section>
        </aside>
      </div>
    </div>
  );
}
