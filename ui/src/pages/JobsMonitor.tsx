import { useEffect, useMemo, useState } from "react";
import { Link } from "react-router-dom";
import type { Job, JobLog } from "../types";

const apiBaseUrl = import.meta.env.VITE_API_BASE_URL ?? "http://localhost:8080";

function formatDate(value?: string | null): string {
  if (!value) return "—";
  return new Date(value).toLocaleString();
}

function statusTone(status: string): string {
  switch (status.toLowerCase()) {
    case "completed":
      return "success";
    case "failed":
      return "error";
    case "running":
      return "primary";
    case "retrying":
      return "warning";
    default:
      return "secondary";
  }
}

function levelTone(level: string): string {
  switch (level.toLowerCase()) {
    case "error":
      return "error";
    case "warning":
      return "warning";
    default:
      return "primary";
  }
}

export default function JobsMonitor() {
  const [jobs, setJobs] = useState<Job[]>([]);
  const [logs, setLogs] = useState<JobLog[]>([]);
  const [selectedJobId, setSelectedJobId] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [logsLoading, setLogsLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const selectedJob = useMemo(
    () => jobs.find((job) => job.id === selectedJobId) ?? null,
    [jobs, selectedJobId]
  );

  useEffect(() => {
    void loadJobs();
  }, []);

  useEffect(() => {
    if (!selectedJobId) {
      setLogs([]);
      return;
    }

    void loadLogs(selectedJobId);
  }, [selectedJobId]);

  async function loadJobs(): Promise<void> {
    try {
      setLoading(true);
      const response = await fetch(`${apiBaseUrl}/jobs`);
      if (!response.ok) {
        throw new Error(`Jobs request failed with status ${response.status}`);
      }

      const data = (await response.json()) as Job[];
      setJobs(data);
      setSelectedJobId((current) => current ?? data[0]?.id ?? null);
      setError(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to load jobs");
    } finally {
      setLoading(false);
    }
  }

  async function loadLogs(jobId: string): Promise<void> {
    try {
      setLogsLoading(true);
      const response = await fetch(`${apiBaseUrl}/jobs/${jobId}/logs`);
      if (!response.ok) {
        throw new Error(`Logs request failed with status ${response.status}`);
      }

      const data = (await response.json()) as JobLog[];
      setLogs(data);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to load logs");
    } finally {
      setLogsLoading(false);
    }
  }

  async function handleRefresh(): Promise<void> {
    await loadJobs();
    if (selectedJobId) {
      await loadLogs(selectedJobId);
    }
  }

  return (
    <div className="page-content">
      <div className="page-header-section">
        <div>
          <h1 className="page-title">Jobs Monitor</h1>
          <p className="page-subtitle">Track execution status, latest logs and failures in one place.</p>
        </div>
        <div className="header-actions">
          <button type="button" className="btn btn-primary" onClick={handleRefresh}>
            Refresh
          </button>
          <Link to="/playlists" className="btn btn-secondary">Back to Playlists</Link>
        </div>
      </div>

      {error && (
        <div className="alert alert-error">
          <strong>Error:</strong> {error}
        </div>
      )}

      <div className="jobs-monitor-layout">
        <section className="jobs-monitor-sidebar">
          <div className="jobs-panel-header">
            <h2 className="section-title">Latest Jobs</h2>
            <span className="jobs-count">{jobs.length}</span>
          </div>

          {loading ? (
            <div className="loading-state">
              <div className="spinner"></div>
              <p>Loading jobs...</p>
            </div>
          ) : jobs.length === 0 ? (
            <div className="empty-state compact-empty">
              <h3>No jobs yet</h3>
              <p>Scheduled jobs will appear here.</p>
            </div>
          ) : (
            <div className="jobs-list">
              {jobs.map((job) => (
                <button
                  key={job.id}
                  type="button"
                  className={`job-list-item ${selectedJobId === job.id ? "is-active" : ""}`}
                  onClick={() => setSelectedJobId(job.id)}
                >
                  <div className="job-list-row">
                    <span className="job-list-type">{job.type}</span>
                    <span className={`badge badge-${statusTone(job.status)}`}>{job.status}</span>
                  </div>
                  <div className="job-list-meta">
                    <span>{job.targetType ?? "—"}</span>
                    <span>{formatDate(job.createdAt)}</span>
                  </div>
                  <div className="job-list-progress">
                    <div className="job-list-progress-track">
                      <div className="job-list-progress-fill" style={{ width: `${Math.max(0, Math.min(100, job.progress))}%` }} />
                    </div>
                    <span>{job.progress}%</span>
                  </div>
                </button>
              ))}
            </div>
          )}
        </section>

        <section className="jobs-monitor-main">
          {!selectedJob ? (
            <div className="empty-state">
              <h3>Select a job</h3>
              <p>Choose a job from the left to inspect its logs and metadata.</p>
            </div>
          ) : (
            <>
              <div className="job-summary-card">
                <div className="job-summary-top">
                  <div>
                    <h2 className="job-summary-title">{selectedJob.type}</h2>
                    <p className="job-summary-id">{selectedJob.id}</p>
                  </div>
                  <span className={`badge badge-${statusTone(selectedJob.status)}`}>{selectedJob.status}</span>
                </div>

                <div className="job-summary-grid">
                  <div className="job-summary-item">
                    <span className="job-summary-label">Target</span>
                    <span className="job-summary-value">{selectedJob.targetType ?? "—"}</span>
                  </div>
                  <div className="job-summary-item">
                    <span className="job-summary-label">Target Id</span>
                    <span className="job-summary-value job-summary-mono">{selectedJob.targetId ?? "—"}</span>
                  </div>
                  <div className="job-summary-item">
                    <span className="job-summary-label">Created</span>
                    <span className="job-summary-value">{formatDate(selectedJob.createdAt)}</span>
                  </div>
                  <div className="job-summary-item">
                    <span className="job-summary-label">Started</span>
                    <span className="job-summary-value">{formatDate(selectedJob.startedAt)}</span>
                  </div>
                  <div className="job-summary-item">
                    <span className="job-summary-label">Finished</span>
                    <span className="job-summary-value">{formatDate(selectedJob.finishedAt)}</span>
                  </div>
                  <div className="job-summary-item">
                    <span className="job-summary-label">Worker</span>
                    <span className="job-summary-value job-summary-mono">{selectedJob.workerId ?? "—"}</span>
                  </div>
                </div>

                <div className="job-progress-block">
                  <div className="job-progress-header">
                    <span>Progress</span>
                    <span>{selectedJob.progress}%</span>
                  </div>
                  <div className="job-list-progress-track">
                    <div className="job-list-progress-fill" style={{ width: `${Math.max(0, Math.min(100, selectedJob.progress))}%` }} />
                  </div>
                </div>

                {selectedJob.errorMessage && (
                  <div className="alert alert-error compact-alert">
                    <strong>{selectedJob.errorCode ?? "Error"}:</strong> {selectedJob.errorMessage}
                  </div>
                )}
              </div>

              <div className="job-logs-card">
                <div className="jobs-panel-header">
                  <h2 className="section-title">Recent Logs</h2>
                  <span className="jobs-count">{logs.length}</span>
                </div>

                {logsLoading ? (
                  <div className="loading-state">
                    <div className="spinner"></div>
                    <p>Loading logs...</p>
                  </div>
                ) : logs.length === 0 ? (
                  <div className="empty-state compact-empty">
                    <h3>No logs yet</h3>
                    <p>This job has not written any logs.</p>
                  </div>
                ) : (
                  <div className="job-log-stream">
                    {logs.map((log) => (
                      <article key={log.id} className="job-log-entry">
                        <div className="job-log-top">
                          <span className={`badge badge-${levelTone(log.level)}`}>{log.level}</span>
                          <time className="job-log-time">{formatDate(log.createdAtUtc)}</time>
                        </div>
                        <p className="job-log-message">{log.message}</p>
                        {log.metadata && (
                          <pre className="job-log-metadata">{log.metadata}</pre>
                        )}
                      </article>
                    ))}
                  </div>
                )}
              </div>
            </>
          )}
        </section>
      </div>
    </div>
  );
}
