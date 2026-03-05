namespace YtProducer.McpRunner.Services;

using System.Diagnostics;
using Microsoft.Extensions.Logging;
using YtProducer.McpRunner.Models;

/// <summary>
/// Manages a child process for an MCP server.
/// Handles start, stop, restart, and stderr capture.
/// </summary>
public class ProcessHost : IAsyncDisposable
{
    private readonly ServiceDefinition _definition;
    private readonly ILogger<ProcessHost> _logger;
    private Process? _process;
    private CancellationTokenSource? _stderrCts;
    private Task? _stderrTask;
    private readonly object _lock = new();

    public string ServiceName => _definition.Name;
    public bool IsRunning => _process?.HasExited == false;

    public event EventHandler<string>? StderrData;

    public ProcessHost(ServiceDefinition definition, ILogger<ProcessHost> logger)
    {
        _definition = definition;
        _logger = logger;
    }

    /// <summary>
    /// Start the MCP server process.
    /// </summary>
    public async Task StartAsync(CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (IsRunning)
                throw new InvalidOperationException($"Process for {ServiceName} is already running.");

            _logger.LogInformation("Starting MCP server: {ServiceName}", ServiceName);

            var psi = new ProcessStartInfo
            {
                FileName = _definition.FileName,
                Arguments = _definition.Arguments,
                WorkingDirectory = _definition.WorkingDirectory ?? Directory.GetCurrentDirectory(),
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            // Apply environment variables
            if (_definition.EnvironmentVariables != null)
            {
                foreach (var kvp in _definition.EnvironmentVariables)
                {
                    psi.EnvironmentVariables[kvp.Key] = kvp.Value;
                }
            }

            _process = Process.Start(psi) ?? throw new InvalidOperationException($"Failed to start process for {ServiceName}");
            _stderrCts = new CancellationTokenSource();

            // Start background task to capture stderr
            _stderrTask = CaptureStderrAsync(_stderrCts.Token);

            _logger.LogInformation("MCP server {ServiceName} started (PID: {Pid})", ServiceName, _process.Id);
        }

        await Task.Delay(100, ct); // Brief delay for process startup
    }

    /// <summary>
    /// Stop the process gracefully.
    /// </summary>
    public async Task StopAsync(TimeSpan? timeout = null)
    {
        lock (_lock)
        {
            if (!IsRunning)
                return;

            _logger.LogInformation("Stopping MCP server: {ServiceName}", ServiceName);

            try
            {
                _process?.StandardInput.Close();
            }
            catch { }

            _stderrCts?.Cancel();
        }

        var waitTimeout = timeout ?? TimeSpan.FromSeconds(5);
        if (_process != null && !_process.WaitForExit((int)waitTimeout.TotalMilliseconds))
        {
            _logger.LogWarning("Force killing MCP server: {ServiceName}", ServiceName);
            _process.Kill();
            _process.WaitForExit(1000);
        }

        if (_stderrTask != null)
        {
            try
            {
                await _stderrTask;
            }
            catch { }
        }
    }

    /// <summary>
    /// Restart the process (stop and start).
    /// </summary>
    public async Task RestartAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Restarting MCP server: {ServiceName}", ServiceName);
        await StopAsync();
        await StartAsync(ct);
    }

    /// <summary>
    /// Get the process stdin writer.
    /// </summary>
    public StreamWriter? GetStdinWriter() => _process?.StandardInput;

    /// <summary>
    /// Get the process stdout reader.
    /// </summary>
    public StreamReader? GetStdoutReader() => _process?.StandardOutput;

    /// <summary>
    /// Check if process is running and healthy.
    /// </summary>
    public bool IsHealthy => IsRunning && !_process!.HasExited;

    private async Task CaptureStderrAsync(CancellationToken ct)
    {
        try
        {
            var reader = _process?.StandardError;
            if (reader == null) return;

            string? line;
            while ((line = await reader.ReadLineAsync(ct)) != null)
            {
                _logger.LogWarning("[{ServiceName}] {StderrLine}", ServiceName, line);
                StderrData?.Invoke(this, line);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error capturing stderr for {ServiceName}", ServiceName);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _process?.Dispose();
        _stderrCts?.Dispose();
        GC.SuppressFinalize(this);
    }
}
