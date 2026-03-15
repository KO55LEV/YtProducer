import { useEffect, useMemo, useState, type Dispatch, type SetStateAction } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import type { PromptGeneration, PromptTemplate, SchedulePromptGenerationRunResponse } from "../types";

const apiBaseUrl = import.meta.env.VITE_API_BASE_URL ?? "http://localhost:8080";

const emptyTemplateForm = {
  name: "",
  slug: "",
  purpose: "playlist_generation",
  description: "",
  notes: "",
  systemPrompt: "",
  userPromptTemplate: "{\n  \"theme\": \"{{theme}}\"\n}",
  inputMode: "theme_only",
  provider: "google",
  model: "gemini-3.1-pro",
  outputMode: "json",
  schemaKey: "",
  settingsJson: "{\n  \"temperature\": 0.7,\n  \"top_p\": 0.95\n}",
  inputContractJson: "{\n  \"required\": [\"theme\"]\n}",
  metadataJson: "{}",
  isActive: true,
  isDefault: false,
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

function formatJsonForDisplay(value?: string | null): string {
  if (!value?.trim()) return "—";
  try {
    return JSON.stringify(JSON.parse(value), null, 2);
  } catch {
    return value;
  }
}

function tryParseJson(value?: string | null): unknown {
  if (!value?.trim()) return null;
  try {
    return JSON.parse(value);
  } catch {
    return value;
  }
}

function resolveUserPromptTemplate(template: PromptTemplate, inputLabel: string, inputJson: string): string {
  const userPromptTemplate = template.userPromptTemplate?.trim()
    || "{\n  \"input_label\": \"{{input_label}}\",\n  \"input_json\": {{input_json}}\n}";

  return userPromptTemplate
    .replace(/{{theme}}/g, inputLabel)
    .replace(/{{input_label}}/g, inputLabel)
    .replace(/{{input_json}}/g, inputJson);
}

async function readApiError(response: Response, fallback: string): Promise<string> {
  try {
    const data = (await response.json()) as { message?: string };
    return data.message?.trim() || fallback;
  } catch {
    return fallback;
  }
}

async function loadPromptTemplate(templateId: string): Promise<PromptTemplate> {
  const response = await fetch(`${apiBaseUrl}/prompt-templates/${templateId}`);
  if (!response.ok) {
    throw new Error(await readApiError(response, `Template request failed with status ${response.status}`));
  }

  return (await response.json()) as PromptTemplate;
}

async function createPromptGeneration(
  template: PromptTemplate,
  request: {
    inputLabel?: string | null;
    inputJson: string;
    model?: string | null;
    resolvedSystemPrompt?: string | null;
    resolvedUserPrompt?: string | null;
    targetType?: string | null;
    targetId?: string | null;
  }
): Promise<PromptGeneration> {
  const response = await fetch(`${apiBaseUrl}/prompt-templates/${template.id}/generations`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(request)
  });

  if (!response.ok) {
    throw new Error(await readApiError(response, `Create generation failed with status ${response.status}`));
  }

  return (await response.json()) as PromptGeneration;
}

async function schedulePromptGenerationRun(generationId: string): Promise<SchedulePromptGenerationRunResponse> {
  const response = await fetch(`${apiBaseUrl}/prompt-generations/${generationId}/run`, {
    method: "POST"
  });

  if (!response.ok) {
    throw new Error(await readApiError(response, `Run prompt failed with status ${response.status}`));
  }

  return (await response.json()) as SchedulePromptGenerationRunResponse;
}

async function savePromptGenerationOutput(
  generationId: string,
  request: {
    outputType?: string | null;
    outputLabel?: string | null;
    outputText?: string | null;
    outputJson?: string | null;
    isPrimary?: boolean;
    isValid?: boolean;
    validationErrors?: string | null;
    providerResponseJson?: string | null;
  }
): Promise<void> {
  const response = await fetch(`${apiBaseUrl}/prompt-generations/${generationId}/outputs`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(request)
  });

  if (!response.ok) {
    throw new Error(await readApiError(response, `Save manual output failed with status ${response.status}`));
  }
}

function TemplateForm({
  templateForm,
  setTemplateForm
}: {
  templateForm: typeof emptyTemplateForm;
  setTemplateForm: Dispatch<SetStateAction<typeof emptyTemplateForm>>;
}) {
  return (
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
        <span>Purpose</span>
        <input value={templateForm.purpose} onChange={(e) => setTemplateForm((c) => ({ ...c, purpose: e.target.value }))} />
      </label>
      <label className="prompt-field">
        <span>Provider</span>
        <input value={templateForm.provider} onChange={(e) => setTemplateForm((c) => ({ ...c, provider: e.target.value }))} />
      </label>
      <label className="prompt-field">
        <span>Input Mode</span>
        <input value={templateForm.inputMode} onChange={(e) => setTemplateForm((c) => ({ ...c, inputMode: e.target.value }))} />
      </label>
      <label className="prompt-field">
        <span>Model</span>
        <input value={templateForm.model} onChange={(e) => setTemplateForm((c) => ({ ...c, model: e.target.value }))} />
      </label>
      <label className="prompt-field">
        <span>Output Mode</span>
        <input value={templateForm.outputMode} onChange={(e) => setTemplateForm((c) => ({ ...c, outputMode: e.target.value }))} />
      </label>
      <label className="prompt-field">
        <span>Schema Key</span>
        <input value={templateForm.schemaKey} onChange={(e) => setTemplateForm((c) => ({ ...c, schemaKey: e.target.value }))} />
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
        <span>Notes</span>
        <textarea rows={4} value={templateForm.notes} onChange={(e) => setTemplateForm((c) => ({ ...c, notes: e.target.value }))} />
      </label>
      <label className="prompt-field prompt-field-full">
        <span>System Prompt</span>
        <textarea
          rows={12}
          value={templateForm.systemPrompt}
          onChange={(e) => setTemplateForm((c) => ({ ...c, systemPrompt: e.target.value }))}
        />
      </label>
      <label className="prompt-field prompt-field-full">
        <span>User Prompt Template</span>
        <textarea
          rows={8}
          value={templateForm.userPromptTemplate}
          onChange={(e) => setTemplateForm((c) => ({ ...c, userPromptTemplate: e.target.value }))}
        />
      </label>
      <label className="prompt-field prompt-field-full">
        <span>Settings JSON</span>
        <textarea rows={6} value={templateForm.settingsJson} onChange={(e) => setTemplateForm((c) => ({ ...c, settingsJson: e.target.value }))} />
      </label>
      <label className="prompt-field prompt-field-full">
        <span>Input Contract JSON</span>
        <textarea rows={6} value={templateForm.inputContractJson} onChange={(e) => setTemplateForm((c) => ({ ...c, inputContractJson: e.target.value }))} />
      </label>
      <label className="prompt-field prompt-field-full">
        <span>Metadata JSON</span>
        <textarea rows={6} value={templateForm.metadataJson} onChange={(e) => setTemplateForm((c) => ({ ...c, metadataJson: e.target.value }))} />
      </label>
      <label className="prompt-field">
        <span>Active</span>
        <input type="checkbox" checked={templateForm.isActive} onChange={(e) => setTemplateForm((c) => ({ ...c, isActive: e.target.checked }))} />
      </label>
      <label className="prompt-field">
        <span>Default</span>
        <input type="checkbox" checked={templateForm.isDefault} onChange={(e) => setTemplateForm((c) => ({ ...c, isDefault: e.target.checked }))} />
      </label>
    </div>
  );
}

export default function PromptsPage() {
  const navigate = useNavigate();
  const [templates, setTemplates] = useState<PromptTemplate[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [search, setSearch] = useState("");
  const [purposeFilter, setPurposeFilter] = useState("all");
  const [providerFilter, setProviderFilter] = useState("all");
  const [statusFilter, setStatusFilter] = useState("active");

  const filteredTemplates = useMemo(() => {
    const term = search.trim().toLowerCase();
    return templates.filter((template) => {
      if (purposeFilter !== "all" && template.purpose !== purposeFilter) return false;
      if (providerFilter !== "all" && template.provider !== providerFilter) return false;
      if (statusFilter === "active" && !template.isActive) return false;
      if (statusFilter === "inactive" && template.isActive) return false;
      if (!term) return true;

      return [
        template.name,
        template.slug,
        template.purpose,
        template.provider,
        template.model ?? "",
        template.description ?? "",
        template.notes ?? ""
      ].some((value) => value.toLowerCase().includes(term));
    });
  }, [templates, search, purposeFilter, providerFilter, statusFilter]);

  const availablePurposes = useMemo(
    () => Array.from(new Set(templates.map((template) => template.purpose))).sort((a, b) => a.localeCompare(b)),
    [templates]
  );
  const availableProviders = useMemo(
    () => Array.from(new Set(templates.map((template) => template.provider))).sort((a, b) => a.localeCompare(b)),
    [templates]
  );

  useEffect(() => {
    void loadTemplates();
  }, []);

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

  return (
    <div className="page-content">
      <div className="page-header-section album-template-index-header">
        <div>
          <h1 className="page-title">Prompts</h1>
          <p className="page-subtitle">Manage reusable LLM prompts for playlist generation, SEO, comments, and future automation flows.</p>
        </div>
        <div className="header-actions">
          <Link to="/prompts/new" className="btn btn-primary">
            New Prompt
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
          <p>Loading prompts...</p>
        </div>
      ) : (
        <>
          <section className="prompt-card-shell prompt-list-filters">
            <div className="prompt-form-grid">
              <label className="prompt-field">
                <span>Search</span>
                <input value={search} onChange={(e) => setSearch(e.target.value)} placeholder="Name, slug, purpose, provider..." />
              </label>
              <label className="prompt-field">
                <span>Purpose</span>
                <select value={purposeFilter} onChange={(e) => setPurposeFilter(e.target.value)}>
                  <option value="all">All</option>
                  {availablePurposes.map((purpose) => (
                    <option key={purpose} value={purpose}>{purpose}</option>
                  ))}
                </select>
              </label>
              <label className="prompt-field">
                <span>Provider</span>
                <select value={providerFilter} onChange={(e) => setProviderFilter(e.target.value)}>
                  <option value="all">All</option>
                  {availableProviders.map((provider) => (
                    <option key={provider} value={provider}>{provider}</option>
                  ))}
                </select>
              </label>
              <label className="prompt-field">
                <span>Status</span>
                <select value={statusFilter} onChange={(e) => setStatusFilter(e.target.value)}>
                  <option value="all">All</option>
                  <option value="active">Active</option>
                  <option value="inactive">Inactive</option>
                </select>
              </label>
            </div>
          </section>

          <section className="prompt-card-shell prompt-list-shell">
            <div className="prompt-list-table-wrap">
              <table className="prompt-list-table prompt-templates-table">
                <colgroup>
                  <col className="prompt-templates-col-prompt" />
                  <col className="prompt-templates-col-purpose" />
                  <col className="prompt-templates-col-provider-model" />
                  <col className="prompt-templates-col-status" />
                  <col className="prompt-templates-col-version" />
                  <col className="prompt-templates-col-updated" />
                  <col className="prompt-templates-col-actions" />
                </colgroup>
                <thead>
                  <tr>
                    <th>Prompt</th>
                    <th>Purpose</th>
                    <th>Provider / Model</th>
                    <th>Status</th>
                    <th>Version</th>
                    <th>Updated</th>
                    <th>Actions</th>
                  </tr>
                </thead>
                <tbody>
                  {filteredTemplates.length === 0 ? (
                    <tr>
                      <td colSpan={7} className="prompt-list-empty">
                        No prompts match the current filters.
                      </td>
                    </tr>
                  ) : (
                    filteredTemplates.map((template) => (
                      <tr key={template.id}>
                        <td>
                          <div className="prompt-list-name">
                            <strong>{template.name}</strong>
                            <span>{template.slug}</span>
                            <p>{template.description || "No description provided."}</p>
                          </div>
                        </td>
                        <td>
                          <span className="prompt-list-pill">{template.purpose}</span>
                        </td>
                        <td>
                          <div className="prompt-list-name">
                            <strong>{template.provider}</strong>
                            <span>{template.model ?? "—"}</span>
                          </div>
                        </td>
                        <td>
                          <div className="prompt-list-badges">
                            <span className={`badge badge-${template.isActive ? "success" : "secondary"}`}>
                              {template.isActive ? "Active" : "Inactive"}
                            </span>
                            {template.isDefault && <span className="badge badge-primary">Default</span>}
                          </div>
                        </td>
                        <td>v{template.version}</td>
                        <td>{formatDate(template.updatedAtUtc)}</td>
                        <td>
                          <div className="prompt-list-actions">
                            <button
                              type="button"
                              className="prompt-list-action-btn prompt-list-action-btn-secondary"
                              onClick={() => navigate(`/prompts/${template.id}/generations`)}
                            >
                              <span aria-hidden="true">◷</span>
                              <span>View</span>
                            </button>
                            <button
                              type="button"
                              className="prompt-list-action-btn"
                              onClick={() => navigate(`/prompts/${template.id}/run`)}
                            >
                              <span aria-hidden="true">▶</span>
                              <span>Run</span>
                            </button>
                            <button
                              type="button"
                              className="prompt-list-action-btn prompt-list-action-btn-secondary"
                              onClick={() => navigate(`/prompts/${template.id}/edit`)}
                            >
                              <span aria-hidden="true">✎</span>
                              <span>Edit</span>
                            </button>
                          </div>
                        </td>
                      </tr>
                    ))
                  )}
                </tbody>
              </table>
            </div>
          </section>
        </>
      )}
    </div>
  );
}

export function PromptEditorPage() {
  const navigate = useNavigate();
  const { id } = useParams<{ id?: string }>();
  const isNewTemplate = !id;

  const [templateForm, setTemplateForm] = useState(emptyTemplateForm);
  const [template, setTemplate] = useState<PromptTemplate | null>(null);
  const [loading, setLoading] = useState(!isNewTemplate);
  const [savingTemplate, setSavingTemplate] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (isNewTemplate) {
      setTemplateForm(emptyTemplateForm);
      setTemplate(null);
      setLoading(false);
      return;
    }

    void loadTemplate(id);
  }, [id, isNewTemplate]);

  async function loadTemplate(templateId?: string): Promise<void> {
    if (!templateId) return;

    try {
      setLoading(true);
      const response = await fetch(`${apiBaseUrl}/prompt-templates/${templateId}`);
      if (!response.ok) {
        throw new Error(`Template request failed with status ${response.status}`);
      }

      const data = (await response.json()) as PromptTemplate;
      setTemplate(data);
      setTemplateForm({
        name: data.name,
        slug: data.slug,
        purpose: data.purpose,
        description: data.description ?? "",
        notes: data.notes ?? "",
        systemPrompt: data.systemPrompt ?? "",
        userPromptTemplate: data.userPromptTemplate ?? "{\n  \"theme\": \"{{theme}}\"\n}",
        inputMode: data.inputMode,
        provider: data.provider,
        model: data.model ?? "",
        outputMode: data.outputMode,
        schemaKey: data.schemaKey ?? "",
        settingsJson: data.settingsJson,
        inputContractJson: data.inputContractJson,
        metadataJson: data.metadataJson,
        isActive: data.isActive,
        isDefault: data.isDefault,
        sortOrder: data.sortOrder
      });
      setError(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to load template");
    } finally {
      setLoading(false);
    }
  }

  async function handleSaveTemplate(): Promise<void> {
    try {
      setSavingTemplate(true);
      setError(null);

      const payload = {
        ...templateForm,
        model: templateForm.model || null,
        schemaKey: templateForm.schemaKey || null
      };

      const response = await fetch(
        template ? `${apiBaseUrl}/prompt-templates/${template.id}` : `${apiBaseUrl}/prompt-templates`,
        {
          method: template ? "PUT" : "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify(payload)
        }
      );

      if (!response.ok) {
        throw new Error(await readApiError(response, `Save template failed with status ${response.status}`));
      }

      const saved = (await response.json()) as PromptTemplate;
      navigate(`/prompts/${saved.id}/edit`);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to save template");
    } finally {
      setSavingTemplate(false);
    }
  }

  if (loading) {
    return (
      <div className="page-content">
        <div className="loading-state">
          <div className="spinner"></div>
          <p>Loading template...</p>
        </div>
      </div>
    );
  }

  return (
    <div className="page-content">
      <div className="page-header-section album-template-detail-header">
        <div>
          <Link to="/prompts" className="breadcrumb">
            ← Back to Prompts
          </Link>
          <h1 className="page-title">{template?.name ?? "New Prompt"}</h1>
          <p className="page-subtitle">Create or edit the reusable prompt definition, provider settings, and flexible JSON contracts.</p>
        </div>
        <div className="header-actions">
          {template && (
            <Link to={`/prompts/${template.id}/generations`} className="btn btn-secondary">
              Prompt Runs
            </Link>
          )}
          <button type="button" className="btn btn-primary" onClick={handleSaveTemplate} disabled={savingTemplate}>
            {savingTemplate ? "Saving..." : "Save Prompt"}
          </button>
        </div>
      </div>

      {error && (
        <div className="alert alert-error">
          <strong>Error:</strong> {error}
        </div>
      )}

      <div className="template-page-stack">
        <section className="prompt-card-shell">
          <div className="prompt-card-header">
            <div>
              <h2 className="section-title">Prompt Editor</h2>
              <p className="prompt-card-subtitle">Edit the reusable prompt, provider/model target, and extensible JSON settings in one place.</p>
            </div>
          </div>

          <TemplateForm templateForm={templateForm} setTemplateForm={setTemplateForm} />
        </section>
      </div>
    </div>
  );
}

export function PromptGenerationsPage() {
  const { id } = useParams<{ id: string }>();
  const [template, setTemplate] = useState<PromptTemplate | null>(null);
  const [generations, setGenerations] = useState<PromptGeneration[]>([]);
  const [statusFilter, setStatusFilter] = useState("all");
  const [search, setSearch] = useState("");
  const [loading, setLoading] = useState(true);
  const [generationsLoading, setGenerationsLoading] = useState(false);
  const [selectedGenerationId, setSelectedGenerationId] = useState<string | null>(null);
  const [modalMode, setModalMode] = useState<"params" | "result" | null>(null);
  const [copiedModalValue, setCopiedModalValue] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const filteredGenerations = useMemo(() => {
    const term = search.trim().toLowerCase();
    return [...generations]
      .filter((generation) => {
        if (statusFilter !== "all" && generation.status.toLowerCase() !== statusFilter) return false;
        if (!term) return true;

        return [
          generation.inputLabel ?? "",
          generation.status,
          generation.model ?? "",
          generation.provider,
          generation.purpose,
          generation.targetType ?? "",
          generation.targetId ?? ""
        ].some((value) => value.toLowerCase().includes(term));
      })
      .sort((a, b) => new Date(b.createdAtUtc).getTime() - new Date(a.createdAtUtc).getTime());
  }, [generations, search, statusFilter]);

  const selectedGeneration = useMemo(
    () => generations.find((item) => item.id === selectedGenerationId) ?? null,
    [generations, selectedGenerationId]
  );

  useEffect(() => {
    void loadTemplateAndGenerations();
  }, [id]);

  async function loadTemplateAndGenerations(): Promise<void> {
    if (!id) return;

    try {
      setLoading(true);
      const templateData = await loadPromptTemplate(id);
      setTemplate(templateData);
      setError(null);
      await loadGenerations(id);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to load generation workspace");
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
      setGenerations([...data].sort((a, b) => new Date(b.createdAtUtc).getTime() - new Date(a.createdAtUtc).getTime()));
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to load generations");
    } finally {
      setGenerationsLoading(false);
    }
  }

  async function handleRefreshGenerations(): Promise<void> {
    if (!template) return;
    await loadGenerations(template.id);
  }

  async function handleCopyModalValue(kind: "params" | "result"): Promise<void> {
    if (!selectedGeneration) return;

    const latestOutput = selectedGeneration.outputs.find((output) => output.isPrimary) ?? selectedGeneration.outputs[0] ?? null;
    const value = kind === "params"
      ? JSON.stringify({
          provider: selectedGeneration.provider,
          model: selectedGeneration.model,
          inputLabel: selectedGeneration.inputLabel,
          targetType: selectedGeneration.targetType,
          targetId: selectedGeneration.targetId,
          inputJson: tryParseJson(selectedGeneration.inputJson),
          messages: [
            { role: "system", content: selectedGeneration.resolvedSystemPrompt },
            { role: "user", content: selectedGeneration.resolvedUserPrompt }
          ]
        }, null, 2)
      : (latestOutput?.outputText || latestOutput?.outputJson || "—");

    await navigator.clipboard.writeText(value);
    setCopiedModalValue(kind);
    window.setTimeout(() => setCopiedModalValue((current) => (current === kind ? null : current)), 1200);
  }

  if (loading) {
    return (
      <div className="page-content">
        <div className="loading-state">
          <div className="spinner"></div>
          <p>Loading generation workspace...</p>
        </div>
      </div>
    );
  }

  if (!template) {
    return (
      <div className="page-content">
        <div className="empty-state">
          <h2>Prompt not found</h2>
          <p>The selected template could not be loaded.</p>
          <Link to="/prompts" className="btn btn-secondary">
            Back to Prompts
          </Link>
        </div>
      </div>
    );
  }

  return (
    <div className="page-content">
      <div className="page-header-section prompt-generations-header">
        <Link to="/prompts" className="breadcrumb">← Back to Prompts</Link>
        <div className="header-actions">
          <button type="button" className="btn btn-primary" onClick={handleRefreshGenerations} disabled={generationsLoading}>
            Refresh
          </button>
        </div>
      </div>

      <section>
        <div className="prompt-generation-filters prompt-generation-filters-wide">
          <label className="prompt-field">
            <span>Search</span>
            <input value={search} onChange={(e) => setSearch(e.target.value)} placeholder="Input label, status, target..." />
          </label>
          <label className="prompt-field">
            <span>Status</span>
            <select value={statusFilter} onChange={(e) => setStatusFilter(e.target.value)}>
              <option value="all">All</option>
              <option value="pending">Pending</option>
              <option value="running">Running</option>
              <option value="completed">Completed</option>
              <option value="failed">Failed</option>
            </select>
          </label>
        </div>

        <div className="prompt-list-table-wrap">
          <table className="prompt-list-table prompt-generations-table">
            <colgroup>
              <col className="prompt-generations-col-model" />
              <col className="prompt-generations-col-params" />
              <col className="prompt-generations-col-status" />
              <col className="prompt-generations-col-usage" />
              <col className="prompt-generations-col-job" />
              <col className="prompt-generations-col-result" />
            </colgroup>
            <thead>
              <tr>
                <th>Model & Details</th>
                <th>Params</th>
                <th>Status</th>
                <th>Usage</th>
                <th>Job ID</th>
                <th>Results</th>
              </tr>
            </thead>
            <tbody>
              {error ? (
                <tr>
                  <td colSpan={6} className="prompt-list-empty prompt-list-error-row">{error}</td>
                </tr>
              ) : generationsLoading ? (
                <tr>
                  <td colSpan={6} className="prompt-list-empty">Loading runs...</td>
                </tr>
              ) : filteredGenerations.length === 0 ? (
                <tr>
                  <td colSpan={6} className="prompt-list-empty">No generations yet.</td>
                </tr>
              ) : (
                filteredGenerations.map((generation) => {
                  const primaryOutput = generation.outputs.find((output) => output.isPrimary) ?? generation.outputs[0] ?? null;
                  const usage = tryParseJson(generation.tokenUsageJson) as { TotalTokens?: number } | null;

                  return (
                    <tr key={generation.id}>
                      <td>
                        <div className="prompt-list-name">
                          <strong>{generation.model ?? "—"}</strong>
                          <span>{formatDate(generation.createdAtUtc)}</span>
                          <p>{generation.inputLabel || generation.purpose}</p>
                        </div>
                      </td>
                      <td>
                        <button
                          type="button"
                          className="prompt-inline-action"
                          onClick={() => {
                            setSelectedGenerationId(generation.id);
                            setModalMode("params");
                          }}
                        >
                          View Params
                        </button>
                      </td>
                      <td>
                        <span className={`badge badge-${generationTone(generation.status)}`}>{generation.status}</span>
                      </td>
                      <td>
                        {usage && typeof usage === "object" ? (
                          <div className="prompt-usage-cell">
                            <span>{typeof usage.TotalTokens === "number" ? usage.TotalTokens : "—"} tokens</span>
                          </div>
                        ) : (
                          "—"
                        )}
                      </td>
                      <td className="prompt-job-id-cell">{generation.jobId ?? "—"}</td>
                      <td>
                        {primaryOutput || generation.errorMessage ? (
                          <button
                            type="button"
                            className="prompt-inline-action"
                            onClick={() => {
                              setSelectedGenerationId(generation.id);
                              setModalMode("result");
                            }}
                          >
                            Result
                          </button>
                        ) : (
                          "—"
                        )}
                      </td>
                    </tr>
                  );
                })
              )}
            </tbody>
          </table>
        </div>
      </section>

      {modalMode && selectedGeneration && (
        <div className="prompts-modal" role="dialog" aria-modal="true" onClick={() => setModalMode(null)}>
          <div className="prompts-modal-content prompts-modal-content-narrow" onClick={(event) => event.stopPropagation()}>
            <div className="prompts-modal-header">
              <div>
                <h3>{modalMode === "params" ? "Parameters" : "Result"}</h3>
                <p>{selectedGeneration.model ?? selectedGeneration.provider}</p>
              </div>
              <div className="prompt-modal-actions">
                <button
                  type="button"
                  className="prompt-copy-btn"
                  onClick={() => void handleCopyModalValue(modalMode)}
                >
                  {copiedModalValue === modalMode ? "Copied" : "Copy"}
                </button>
                <button
                  type="button"
                  className="prompts-modal-close"
                  onClick={() => setModalMode(null)}
                  aria-label="Close modal"
                >
                  ×
                </button>
              </div>
            </div>

            {modalMode === "params" ? (
              <div className="prompt-modal-sections">
                <article className="prompt-card">
                  <div className="prompt-card-header">
                    <div className="prompt-card-title">
                      <span className="prompt-card-track">Request</span>
                    </div>
                  </div>
                  <pre className="prompt-card-text">{JSON.stringify({
                    provider: selectedGeneration.provider,
                    model: selectedGeneration.model,
                    inputLabel: selectedGeneration.inputLabel,
                    targetType: selectedGeneration.targetType,
                    targetId: selectedGeneration.targetId,
                    inputJson: tryParseJson(selectedGeneration.inputJson),
                    messages: [
                      { role: "system", content: selectedGeneration.resolvedSystemPrompt },
                      { role: "user", content: selectedGeneration.resolvedUserPrompt }
                    ]
                  }, null, 2)}</pre>
                </article>
              </div>
            ) : (
              <div className="prompt-modal-sections">
                {(() => {
                  const primaryOutput = selectedGeneration.outputs.find((output) => output.isPrimary) ?? selectedGeneration.outputs[0] ?? null;
                  return (
                    <article className="prompt-card">
                      <div className="prompt-card-header">
                        <div className="prompt-card-title">
                          <span className="prompt-card-track">Response</span>
                        </div>
                      </div>
                      <pre className="prompt-card-text">
                        {primaryOutput?.outputText
                          || primaryOutput?.outputJson
                          || selectedGeneration.errorMessage
                          || "No response saved."}
                      </pre>
                    </article>
                  );
                })()}
              </div>
            )}
          </div>
        </div>
      )}
    </div>
  );
}

export function PromptRunPage() {
  const navigate = useNavigate();
  const { id } = useParams<{ id: string }>();
  const [template, setTemplate] = useState<PromptTemplate | null>(null);
  const [inputLabel, setInputLabel] = useState("");
  const [inputJson, setInputJson] = useState("{\n  \n}");
  const [model, setModel] = useState("");
  const [resolvedSystemPrompt, setResolvedSystemPrompt] = useState("");
  const [resolvedUserPrompt, setResolvedUserPrompt] = useState("");
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [showInputPromptPreview, setShowInputPromptPreview] = useState(false);
  const [copiedInputPrompt, setCopiedInputPrompt] = useState(false);

  useEffect(() => {
    if (!id) return;
    void loadTemplate();
  }, [id]);

  async function loadTemplate(): Promise<void> {
    if (!id) return;

    try {
      setLoading(true);
      const templateData = await loadPromptTemplate(id);
      setTemplate(templateData);
      setModel(templateData.model ?? "");
      setResolvedSystemPrompt(templateData.systemPrompt?.trim() ?? "");
      setResolvedUserPrompt(resolveUserPromptTemplate(templateData, "", "{\n  \n}"));
      setError(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to load run page");
    } finally {
      setLoading(false);
    }
  }

  async function handleRunPrompt(): Promise<void> {
    if (!template) return;

    try {
      JSON.parse(inputJson);
    } catch {
      setError("Input JSON must be valid JSON.");
      return;
    }

    try {
      setSaving(true);
      setError(null);

      const created = await createPromptGeneration(template, {
        inputLabel: inputLabel.trim() || null,
        inputJson,
        model: model.trim() || null,
        resolvedSystemPrompt: resolvedSystemPrompt.trim() || null,
        resolvedUserPrompt: resolvedUserPrompt.trim() || null
      });
      await schedulePromptGenerationRun(created.id);
      navigate(`/prompts/${template.id}/generations`);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to schedule prompt run");
    } finally {
      setSaving(false);
    }
  }

  async function handleCopyInputPrompt(): Promise<void> {
    try {
      await navigator.clipboard.writeText(combinedInputPrompt);
      setCopiedInputPrompt(true);
      window.setTimeout(() => setCopiedInputPrompt(false), 1400);
    } catch {
      setCopiedInputPrompt(false);
    }
  }

  const isAlbumGeneration = template?.purpose === "album_generation";
  const combinedInputPrompt = [
    "System Prompt",
    "",
    resolvedSystemPrompt.trim() || "—",
    "",
    "User Prompt",
    "",
    resolvedUserPrompt.trim() || "—"
  ].join("\n");

  if (loading) {
    return (
      <div className="page-content">
        <div className="loading-state">
          <div className="spinner"></div>
          <p>Loading run page...</p>
        </div>
      </div>
    );
  }

  if (!template) {
    return (
      <div className="page-content">
        <div className="empty-state">
          <h2>Prompt not found</h2>
          <p>The selected template could not be loaded.</p>
          <Link to="/prompts" className="btn btn-secondary">
            Back to Prompts
          </Link>
        </div>
      </div>
    );
  }

  return (
    <div className="page-content">
      <div className="page-header-section album-template-detail-header">
        <div>
          <Link to={`/prompts/${template.id}/generations`} className="breadcrumb">
            ← Back to Generations
          </Link>
          <h1 className="page-title">{template.name}</h1>
          <p className="page-subtitle">Review the resolved request, adjust any fields you need, then schedule the LLM run.</p>
        </div>
        <div className="header-actions">
          <Link to={`/prompts/${template.id}/edit`} className="btn btn-secondary">
            Edit Prompt
          </Link>
          {isAlbumGeneration ? (
            <button type="button" className="btn btn-secondary" onClick={() => setShowInputPromptPreview(true)}>
              Input Prompt
            </button>
          ) : null}
          <button type="button" className="btn btn-primary" onClick={handleRunPrompt} disabled={saving}>
            {saving ? "Scheduling..." : "Run Prompt"}
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
                <h2 className="section-title">Run Prompt</h2>
                <p className="prompt-card-subtitle">The worker will execute exactly the resolved system and user prompt you set here.</p>
              </div>
            </div>

            <div className="album-template-generate-grid">
              <div className="prompt-generation-form">
                <label className="prompt-field">
                  <span>Input Label</span>
                  <input value={inputLabel} onChange={(e) => setInputLabel(e.target.value)} placeholder="Manual test run" />
                </label>
                <label className="prompt-field">
                  <span>Input JSON</span>
                  <textarea rows={10} value={inputJson} onChange={(e) => setInputJson(e.target.value)} placeholder='{\n  "theme": "Ultimate Gym Workout Music"\n}' />
                </label>
                <label className="prompt-field">
                  <span>Provider</span>
                  <input value={template.provider} readOnly />
                </label>
                <label className="prompt-field">
                  <span>Model</span>
                  <input value={model} onChange={(e) => setModel(e.target.value)} placeholder="gemini-3.1-pro" />
                </label>
              </div>

              <div className="prompt-generation-form">
                <label className="prompt-field prompt-field-full">
                  <span>Resolved System Prompt</span>
                  <textarea rows={12} value={resolvedSystemPrompt} onChange={(e) => setResolvedSystemPrompt(e.target.value)} />
                </label>
                <label className="prompt-field">
                  <span>Resolved User Prompt</span>
                  <textarea rows={14} value={resolvedUserPrompt} onChange={(e) => setResolvedUserPrompt(e.target.value)} />
                </label>
                <label className="prompt-field">
                  <span>Template Settings JSON</span>
                  <pre className="prompt-code-block prompt-code-block-tall">{formatJsonForDisplay(template.settingsJson)}</pre>
                </label>
              </div>
            </div>
          </section>
        </section>

        <aside className="album-template-sidebar">
          <section className="prompt-card-shell album-template-summary-card">
            <div className="prompt-card-header">
              <div>
                <h2 className="section-title">Prompt Summary</h2>
                <p className="prompt-card-subtitle">Quick context while you store a manual run.</p>
              </div>
            </div>

            <div className="album-template-summary-list">
              <div className="album-template-summary-item">
                <span className="job-summary-label">Template</span>
                <span className="job-summary-value">{template.name}</span>
              </div>
              <div className="album-template-summary-item">
                <span className="job-summary-label">Slug</span>
                <span className="job-summary-value">{template.slug || "—"}</span>
              </div>
              <div className="album-template-summary-item">
                <span className="job-summary-label">Model</span>
                <span className="job-summary-value">{template.model || "—"}</span>
              </div>
              <div className="album-template-summary-item">
                <span className="job-summary-label">Purpose</span>
                <span className="job-summary-value">{template.purpose || "—"}</span>
              </div>
              <div className="album-template-summary-item">
                <span className="job-summary-label">Output Mode</span>
                <span className="job-summary-value">{template.outputMode || "—"}</span>
              </div>
              <div className="album-template-summary-item">
                <span className="job-summary-label">Schema Key</span>
                <span className="job-summary-value">{template.schemaKey || "—"}</span>
              </div>
            </div>
          </section>
        </aside>
      </div>

      {showInputPromptPreview ? (
        <div className="prompts-modal" role="dialog" aria-modal="true" onClick={() => setShowInputPromptPreview(false)}>
          <div className="prompts-modal-content prompts-modal-content-narrow" onClick={(event) => event.stopPropagation()}>
            <div className="prompts-modal-header">
              <div>
                <h3>Input Prompt</h3>
                <p>Preview the exact prompt you can copy into an LLM manually.</p>
              </div>
              <div className="prompt-modal-actions">
                <button
                  type="button"
                  className="prompt-copy-btn"
                  onClick={() => void handleCopyInputPrompt()}
                >
                  {copiedInputPrompt ? "Copied" : "Copy"}
                </button>
                <button
                  type="button"
                  className="prompts-modal-close"
                  onClick={() => setShowInputPromptPreview(false)}
                  aria-label="Close modal"
                >
                  ×
                </button>
              </div>
            </div>

            <div className="prompt-modal-sections">
              <article className="prompt-card">
                <div className="prompt-card-header">
                  <div className="prompt-card-title">
                    <span className="prompt-card-track">Combined Prompt</span>
                  </div>
                </div>
                <pre className="prompt-card-text">{combinedInputPrompt}</pre>
              </article>
            </div>
          </div>
        </div>
      ) : null}
    </div>
  );
}
