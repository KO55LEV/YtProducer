namespace YtProducer.McpRunner.Models;

using System.Text.Json;

/// <summary>
/// Defines an MCP service configuration.
/// </summary>
public record ServiceDefinition
{
    /// <summary>
    /// Service name (e.g., "KieAi", "Media", "YouTube").
    /// </summary>
    public string Name { get; set; } = null!;

    /// <summary>
    /// Executable file name or path to start the service.
    /// </summary>
    public string FileName { get; set; } = null!;

    /// <summary>
    /// Arguments to pass to the executable.
    /// </summary>
    public string Arguments { get; set; } = "";

    /// <summary>
    /// Working directory for the process (optional).
    /// </summary>
    public string? WorkingDirectory { get; set; }

    /// <summary>
    /// Environment variables to set (optional).
    /// </summary>
    public Dictionary<string, string>? EnvironmentVariables { get; set; }

    /// <summary>
    /// Tool allow-list (if null, discover dynamically).
    /// </summary>
    public HashSet<string>? AllowedTools { get; set; }
}

/// <summary>
/// Tool metadata with associated service.
/// </summary>
public record CachedTool
{
    public string Name { get; set; } = null!;
    public string ServiceName { get; set; } = null!;
    public string? Description { get; set; }
    public JsonElement? InputSchema { get; set; }
}
