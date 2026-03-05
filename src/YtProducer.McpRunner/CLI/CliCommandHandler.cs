namespace YtProducer.McpRunner.CLI;

using System.Text.Json;
using System.Text.Encodings.Web;
using Microsoft.Extensions.Logging;
using YtProducer.McpRunner.Services;

/// <summary>
/// Handles CLI commands: list-tools, call
/// Manages output formatting (JSON to stdout, diagnostics to stderr).
/// </summary>
public class CliCommandHandler
{
    private readonly ToolRegistry _toolRegistry;
    private readonly ToolRouter _toolRouter;
    private readonly ILogger<CliCommandHandler> _logger;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public CliCommandHandler(
        ToolRegistry toolRegistry,
        ToolRouter toolRouter,
        ILogger<CliCommandHandler> logger)
    {
        _toolRegistry = toolRegistry;
        _toolRouter = toolRouter;
        _logger = logger;
    }

    /// <summary>
    /// Execute a CLI command.
    /// </summary>
    public async Task<int> ExecuteAsync(string[] args, CancellationToken ct = default)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return 1;
        }

        var command = args[0];

        try
        {
            return command switch
            {
                "list-tools" => await ListToolsAsync(args.Skip(1).ToArray(), ct),
                "call" => await CallToolAsync(args.Skip(1).ToArray(), ct),
                "--help" or "-h" => PrintUsage() ?? 0,
                _ => throw new InvalidOperationException($"Unknown command: {command}")
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Command execution failed");
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    /// <summary>
    /// Handle "list-tools <service|all>" command.
    /// </summary>
    private async Task<int> ListToolsAsync(string[] args, CancellationToken ct)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Usage: list-tools <service|all>");
            return 1;
        }

        var target = args[0];

        if (target.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            await _toolRegistry.RefreshAllToolsAsync(ct);
        }
        else
        {
            await _toolRegistry.RefreshToolsForServiceAsync(target, ct);
        }

        var tools = target.Equals("all", StringComparison.OrdinalIgnoreCase)
            ? _toolRegistry.GetAllTools()
            : _toolRegistry.GetToolsByService(target);

        var toolList = tools.Select(t => new
        {
            name = t.Name,
            service = t.ServiceName,
            description = t.Description,
            inputSchema = t.InputSchema != null ? JsonSerializer.Deserialize<object>(t.InputSchema.Value.GetRawText()) : null
        }).ToList();

        var output = new
        {
            ok = true,
            data = toolList,
            meta = new { count = toolList.Count, target }
        };

        PrintJson(output);
        return 0;
    }

    /// <summary>
    /// Handle "call <tool_name> --args '<json>'" command.
    /// </summary>
    private async Task<int> CallToolAsync(string[] args, CancellationToken ct)
    {
        if (args.Length < 3 || args[1] != "--args")
        {
            Console.Error.WriteLine("Usage: call <tool_name> --args '<json_args>' [--timeout <seconds>] [--raw]");
            return 1;
        }

        var toolName = args[0];
        var argsJson = args[2];
        var timeout = TimeSpan.FromSeconds(30);
        var preserveRaw = false;

        // Parse optional arguments
        for (int i = 3; i < args.Length; i++)
        {
            if (args[i] == "--timeout" && i + 1 < args.Length)
            {
                if (int.TryParse(args[++i], out var seconds))
                    timeout = TimeSpan.FromSeconds(seconds);
            }
            else if (args[i] == "--raw")
            {
                preserveRaw = true;
            }
        }

        // Parse arguments JSON
        JsonElement? parsedArgs = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(argsJson) && argsJson != "{}")
            {
                parsedArgs = JsonSerializer.Deserialize<JsonElement>(argsJson);
            }
        }
        catch (JsonException ex)
        {
            Console.Error.WriteLine($"Invalid JSON arguments: {ex.Message}");
            return 1;
        }

        // Call the tool
        var result = await _toolRouter.CallToolAsync(toolName, parsedArgs, timeout, preserveRaw, ct);

        PrintJson(result);
        return result.Ok ? 0 : 1;
    }

    /// <summary>
    /// Print JSON to stdout.
    /// </summary>
    private void PrintJson(object obj)
    {
        var json = JsonSerializer.Serialize(obj, JsonOptions);
        Console.WriteLine(json);
    }

    /// <summary>
    /// Print usage information.
    /// </summary>
    private static int? PrintUsage()
    {
        Console.Error.WriteLine(@"
MCP Client Runner - CLI Interface

Commands:
  list-tools <service|all>
    List all tools from a service or all services.
    Example: list-tools all
             list-tools YouTube

  call <tool_name> --args '<json>' [--timeout <seconds>] [--raw]
    Call a specific tool with JSON arguments.
    Example: call suno_generate_music --args '{""title"":""Song""}'
             call youtube.upload_video --args '{...}' --timeout 60 --raw

Options:
  --timeout <seconds>   Request timeout in seconds (default: 30)
  --raw                 Include raw JSON-RPC response in output

Exit Codes:
  0  Success (tool call returned ok=true)
  1  Failure (error or tool not found)

Examples:
  # List all available tools
  dotnet run -- list-tools all

  # List tools from specific service
  dotnet run -- list-tools KieAi

  # Call a tool with arguments
  dotnet run -- call image_generation --args '{""prompt"":""a cat""}'

  # Call with custom timeout and preserve raw response
  dotnet run -- call youtube.upload_video --args '{...}' --timeout 120 --raw
");
        return 0;
    }
}
