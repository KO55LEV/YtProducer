import { useEffect, useMemo, useState } from "react";
import { Link, useSearchParams } from "react-router-dom";
import type { Playlist, PlaylistMediaResponse, PlaylistTrackMedia, YoutubeVideoEngagement } from "../types";

const apiBaseUrl = import.meta.env.VITE_API_BASE_URL ?? "http://localhost:8080";

type ModalState =
  | { mode: "generated"; engagementId: string }
  | { mode: "final"; engagementId: string }
  | null;

type TrackDetails = {
  title: string;
  playlistPosition?: number | null;
};

function formatDate(value?: string | null): string {
  if (!value) return "—";
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return value;
  return date.toLocaleString("en-GB", {
    day: "2-digit",
    month: "2-digit",
    year: "numeric",
    hour: "2-digit",
    minute: "2-digit",
    second: "2-digit"
  });
}

function truncateMiddle(value: string, maxLength = 24): string {
  if (value.length <= maxLength) return value;
  const side = Math.max(4, Math.floor((maxLength - 3) / 2));
  return `${value.slice(0, side)}...${value.slice(-side)}`;
}

function safePrettyJson(value?: string | null): string {
  if (!value?.trim()) return "{}";

  try {
    return JSON.stringify(JSON.parse(value), null, 2);
  } catch {
    return value;
  }
}

function parseMetadataJson(value?: string | null): Record<string, unknown> | null {
  if (!value?.trim()) return null;

  try {
    const parsed = JSON.parse(value);
    return parsed && typeof parsed === "object" && !Array.isArray(parsed)
      ? (parsed as Record<string, unknown>)
      : null;
  } catch {
    return null;
  }
}

function readStringField(record: Record<string, unknown> | null, key: string): string | null {
  const value = record?.[key];
  return typeof value === "string" && value.trim() ? value.trim() : null;
}

function pickTrackImage(media?: PlaylistTrackMedia | null): string | null {
  if (!media || media.images.length === 0) return null;
  return media.images[0]?.url ?? null;
}

function statusTone(status: string): string {
  switch (status.toLowerCase()) {
    case "posted":
      return "success";
    case "generated":
      return "processing";
    case "failed":
      return "error";
    default:
      return "secondary";
  }
}

export default function YoutubeEngagementsPage() {
  const [searchParams] = useSearchParams();
  const playlistIdParam = searchParams.get("playlistId")?.trim() ?? "";
  const [engagements, setEngagements] = useState<YoutubeVideoEngagement[]>([]);
  const [playlist, setPlaylist] = useState<Playlist | null>(null);
  const [mediaByPosition, setMediaByPosition] = useState<Record<number, PlaylistTrackMedia>>({});
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [search, setSearch] = useState("");
  const [statusFilter, setStatusFilter] = useState("all");
  const [typeFilter, setTypeFilter] = useState("all");
  const [modal, setModal] = useState<ModalState>(null);
  const [copied, setCopied] = useState(false);
  const [editableMessage, setEditableMessage] = useState("");
  const [savingMessage, setSavingMessage] = useState(false);
  const [saveError, setSaveError] = useState<string | null>(null);
  const [postingById, setPostingById] = useState<Record<string, boolean>>({});

  useEffect(() => {
    void fetchEngagements();
  }, [playlistIdParam]);

  async function fetchEngagements(): Promise<void> {
    try {
      setLoading(true);
      const requestUrl = new URL(`${apiBaseUrl}/youtube-video-engagements`);
      if (playlistIdParam) {
        requestUrl.searchParams.set("playlistId", playlistIdParam);
      }

      const engagementsPromise = fetch(requestUrl.toString());
      const playlistPromise = playlistIdParam
        ? fetch(`${apiBaseUrl}/playlists/${playlistIdParam}`)
        : Promise.resolve<Response | null>(null);
      const mediaPromise = playlistIdParam
        ? fetch(`${apiBaseUrl}/playlists/${playlistIdParam}/media`)
        : Promise.resolve<Response | null>(null);

      const [engagementsResponse, playlistResponse, mediaResponse] = await Promise.all([
        engagementsPromise,
        playlistPromise,
        mediaPromise
      ]);

      if (!engagementsResponse.ok) {
        throw new Error(`Request failed with status ${engagementsResponse.status}`);
      }

      const data = (await engagementsResponse.json()) as YoutubeVideoEngagement[];
      setEngagements(data);

      if (playlistResponse?.ok) {
        setPlaylist((await playlistResponse.json()) as Playlist);
      } else {
        setPlaylist(null);
      }

      if (mediaResponse?.ok) {
        const media = (await mediaResponse.json()) as PlaylistMediaResponse;
        const mediaMap: Record<number, PlaylistTrackMedia> = {};
        for (const track of media.tracks) {
          mediaMap[track.playlistPosition] = track;
        }
        setMediaByPosition(mediaMap);
      } else {
        setMediaByPosition({});
      }

      setError(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Unknown error");
    } finally {
      setLoading(false);
    }
  }

  const engagementTypes = useMemo(
    () => Array.from(new Set(engagements.map((item) => item.engagementType).filter(Boolean))).sort(),
    [engagements]
  );

  const filteredEngagements = useMemo(() => {
    const term = search.trim().toLowerCase();

    return engagements.filter((item) => {
      if (statusFilter !== "all" && item.status.toLowerCase() !== statusFilter) {
        return false;
      }

      if (typeFilter !== "all" && item.engagementType !== typeFilter) {
        return false;
      }

      if (!term) {
        return true;
      }

      const haystack = [
        item.channelId,
        item.youtubeVideoId,
        item.engagementType,
        item.status,
        item.provider ?? "",
        item.model ?? "",
        item.youtubeCommentId ?? "",
        item.trackId ?? "",
        item.playlistId ?? "",
        item.albumReleaseId ?? ""
      ].join(" ").toLowerCase();

      return haystack.includes(term);
    });
  }, [engagements, search, statusFilter, typeFilter]);

  const trackDetailsById = useMemo(() => {
    const map: Record<string, TrackDetails> = {};
    for (const track of playlist?.tracks ?? []) {
      map[track.id] = {
        title: track.youTubeTitle?.trim() || track.title,
        playlistPosition: track.playlistPosition
      };
    }
    return map;
  }, [playlist]);

  const selectedEngagement = modal
    ? engagements.find((item) => item.id === modal.engagementId) ?? null
    : null;

  const modalTitle = modal?.mode === "generated" ? "Generated Message" : "Final Message";
  const modalValue = selectedEngagement
    ? modal?.mode === "generated"
      ? selectedEngagement.generatedText ?? ""
      : selectedEngagement.finalText ?? ""
    : "";
  const isFinalMessageModal = modal?.mode === "final";
  const isMessageDirty = isFinalMessageModal && selectedEngagement
    ? editableMessage !== (selectedEngagement.finalText ?? "")
    : false;

  useEffect(() => {
    setEditableMessage(modalValue);
    setSaveError(null);
  }, [modalValue, modal]);

  async function handleCopy(value: string): Promise<void> {
    if (!value) return;

    try {
      await navigator.clipboard.writeText(value);
      setCopied(true);
      window.setTimeout(() => setCopied(false), 1400);
    } catch {
      setCopied(false);
    }
  }

  async function handleSaveMessage(): Promise<void> {
    if (!selectedEngagement || !isFinalMessageModal || !isMessageDirty) {
      return;
    }

    try {
      setSavingMessage(true);
      setSaveError(null);

      const response = await fetch(`${apiBaseUrl}/youtube-video-engagements/${selectedEngagement.id}`, {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          finalText: editableMessage
        })
      });

      if (!response.ok) {
        throw new Error(`Save failed with status ${response.status}`);
      }

      const updated = (await response.json()) as YoutubeVideoEngagement;
      setEngagements((current) => current.map((item) => (item.id === updated.id ? updated : item)));
      setEditableMessage(updated.finalText ?? "");
      setModal(null);
    } catch (err) {
      setSaveError(err instanceof Error ? err.message : "Unable to save message");
    } finally {
      setSavingMessage(false);
    }
  }

  async function handlePostEngagement(engagementId: string): Promise<void> {
    try {
      setPostingById((current) => ({ ...current, [engagementId]: true }));

      const response = await fetch(`${apiBaseUrl}/youtube-video-engagements/${engagementId}/post`, {
        method: "POST"
      });

      if (!response.ok) {
        throw new Error(`Post scheduling failed with status ${response.status}`);
      }

      await fetchEngagements();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Unable to schedule comment posting");
    } finally {
      setPostingById((current) => ({ ...current, [engagementId]: false }));
    }
  }

  return (
    <div className="page-content">
      <div className="page-header-section prompt-generations-header">
        <Link to={playlistIdParam ? `/playlists/${playlistIdParam}` : "/"} className="breadcrumb">
          ← {playlistIdParam ? "Back to Playlist" : "Back to Home"}
        </Link>
        <div className="header-actions">
          <button type="button" className="btn btn-primary" onClick={() => void fetchEngagements()} disabled={loading}>
            Refresh
          </button>
        </div>
      </div>

      <section>
        <div className="prompt-generation-filters prompt-engagement-filters">
          <label className="prompt-field">
            <span>Search</span>
            <input
              value={search}
              onChange={(event) => setSearch(event.target.value)}
              placeholder="Channel, video, type, provider, track..."
            />
          </label>
          <label className="prompt-field">
            <span>Status</span>
            <select value={statusFilter} onChange={(event) => setStatusFilter(event.target.value)}>
              <option value="all">All</option>
              <option value="draft">Draft</option>
              <option value="generated">Generated</option>
              <option value="posted">Posted</option>
              <option value="failed">Failed</option>
            </select>
          </label>
          <label className="prompt-field">
            <span>Type</span>
            <select value={typeFilter} onChange={(event) => setTypeFilter(event.target.value)}>
              <option value="all">All</option>
              {engagementTypes.map((type) => (
                <option key={type} value={type}>{type}</option>
              ))}
            </select>
          </label>
        </div>

        <div className="prompt-list-table-wrap">
          <table className="prompt-list-table prompt-engagements-table">
            <colgroup>
              <col className="prompt-engagements-col-video" />
              <col className="prompt-engagements-col-type" />
              <col className="prompt-engagements-col-status" />
              <col className="prompt-engagements-col-generated" />
              <col className="prompt-engagements-col-final" />
              <col className="prompt-engagements-col-comment" />
            </colgroup>
            <thead>
              <tr>
                <th>Video</th>
                <th>Details</th>
                <th>Status</th>
                <th>Generated</th>
                <th>Final</th>
                <th>Comment</th>
              </tr>
            </thead>
            <tbody>
              {error ? (
                <tr>
                  <td colSpan={6} className="prompt-list-empty prompt-list-error-row">{error}</td>
                </tr>
              ) : loading ? (
                <tr>
                  <td colSpan={6} className="prompt-list-empty">Loading engagements...</td>
                </tr>
              ) : filteredEngagements.length === 0 ? (
                <tr>
                  <td colSpan={6} className="prompt-list-empty">No engagements found.</td>
                </tr>
              ) : (
                filteredEngagements.map((item) => (
                  <tr key={item.id}>
                    {(() => {
                      const metadata = parseMetadataJson(item.metadataJson);
                      const sourceTrackId = item.trackId ?? null;
                      const sourceTrack = sourceTrackId ? trackDetailsById[sourceTrackId] : null;
                      const playlistPosition =
                        sourceTrack?.playlistPosition ??
                        (typeof metadata?.playlistPosition === "number" ? metadata.playlistPosition : null);
                      const media = playlistPosition ? mediaByPosition[playlistPosition] : null;
                      const imageUrl = pickTrackImage(media);
                      const trackTitle =
                        sourceTrack?.title ??
                        readStringField(metadata, "trackTitle") ??
                        readStringField(metadata, "youtubeTitle") ??
                        "Unknown track";
                      const publishedAt = item.postedAtUtc ?? item.createdAtUtc;

                      return (
                        <>
                    <td>
                      <div className="prompt-engagement-video-cell">
                        {imageUrl ? (
                          <img
                            className="prompt-engagement-thumb"
                            src={imageUrl.startsWith("http") ? imageUrl : `${apiBaseUrl}${imageUrl}`}
                            alt={trackTitle}
                            loading="lazy"
                          />
                        ) : (
                          <div className="prompt-engagement-thumb prompt-engagement-thumb-placeholder" aria-hidden="true">
                            {trackTitle.slice(0, 1).toUpperCase()}
                          </div>
                        )}
                        <div className="prompt-list-name prompt-list-name-compact">
                          <strong>{trackTitle}</strong>
                          <span>
                            <a
                              className="prompt-engagement-video-link"
                              href={`https://www.youtube.com/watch?v=${encodeURIComponent(item.youtubeVideoId)}`}
                              target="_blank"
                              rel="noreferrer"
                              title={item.youtubeVideoId}
                            >
                              {item.youtubeVideoId}
                            </a>
                          </span>
                        </div>
                      </div>
                    </td>
                    <td>
                      <div className="prompt-list-name">
                        <strong title={item.engagementType}>{truncateMiddle(item.engagementType, 24)}</strong>
                        <span>{item.provider ?? "—"}{item.model ? ` / ${item.model}` : ""}</span>
                        <p>Published {formatDate(publishedAt)}</p>
                        <span title={item.trackId ?? item.albumReleaseId ?? item.playlistId ?? "—"}>
                          {truncateMiddle(item.trackId ?? item.albumReleaseId ?? item.playlistId ?? "—", 18)}
                        </span>
                      </div>
                    </td>
                    <td>
                      <span
                        className={`badge badge-${statusTone(item.status)}`}
                        title={item.status}
                      >
                        {truncateMiddle(item.status, 14)}
                      </span>
                    </td>
                    <td>
                      {item.generatedText ? (
                        <button
                          type="button"
                          className="prompt-inline-action"
                          onClick={() => setModal({ mode: "generated", engagementId: item.id })}
                        >
                          View
                        </button>
                      ) : "—"}
                    </td>
                    <td>
                      {item.finalText ? (
                        <button
                          type="button"
                          className="prompt-inline-action"
                          onClick={() => setModal({ mode: "final", engagementId: item.id })}
                        >
                          View
                        </button>
                      ) : "—"}
                    </td>
                    <td>
                      <div className="prompt-list-name">
                        <strong>{item.youtubeCommentId ? truncateMiddle(item.youtubeCommentId, 18) : "—"}</strong>
                        <span>{item.postedAtUtc ? formatDate(item.postedAtUtc) : "Not posted"}</span>
                        <p>{item.errorMessage ?? "—"}</p>
                        {!item.youtubeCommentId && item.finalText && item.status.toLowerCase() !== "posted" ? (
                          <button
                            type="button"
                            className="prompt-inline-action"
                            onClick={() => void handlePostEngagement(item.id)}
                            disabled={postingById[item.id] === true}
                          >
                            {postingById[item.id] === true ? "Posting..." : "Post"}
                          </button>
                        ) : null}
                      </div>
                    </td>
                        </>
                      );
                    })()}
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>
      </section>

      {modal && selectedEngagement && (
        <div className="prompts-modal" role="presentation" onClick={() => setModal(null)}>
          <div
            className="prompts-modal-content prompts-modal-content-narrow"
            role="dialog"
            aria-modal="true"
            aria-label={modalTitle}
            onClick={(event) => event.stopPropagation()}
          >
            <div className="prompts-modal-header">
              <h3>{modalTitle}</h3>
              <div className="prompt-modal-actions">
                {isFinalMessageModal && isMessageDirty ? (
                  <button
                    type="button"
                    className="prompt-copy-btn"
                    onClick={() => void handleSaveMessage()}
                    disabled={savingMessage}
                  >
                    {savingMessage ? "Saving..." : "Save"}
                  </button>
                ) : null}
                <button
                  type="button"
                  className="prompt-copy-btn"
                  onClick={() => void handleCopy(modalValue)}
                  disabled={!modalValue}
                >
                  {copied ? "Copied" : "Copy"}
                </button>
                <button type="button" className="prompts-modal-close" onClick={() => setModal(null)}>Close</button>
              </div>
            </div>
            <div className="prompt-modal-sections">
              <div className="prompt-card">
                <div className="prompt-card-header-inline">
                  <strong>Message</strong>
                </div>
                {isFinalMessageModal ? (
                  <>
                    <textarea
                      className="prompt-message-editor"
                      value={editableMessage}
                      onChange={(event) => setEditableMessage(event.target.value)}
                      placeholder="Enter final message"
                      rows={6}
                    />
                    {saveError ? <p className="prompt-save-error">{saveError}</p> : null}
                  </>
                ) : (
                  <pre className="prompt-code-block prompt-code-block-tall">{modalValue || "No content available."}</pre>
                )}
              </div>
              <div className="prompt-card">
                <div className="prompt-card-header-inline">
                  <strong>Metadata</strong>
                </div>
                <pre className="prompt-code-block">{safePrettyJson(selectedEngagement.metadataJson)}</pre>
              </div>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
