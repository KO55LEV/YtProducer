namespace YtProducer.Media.Services;

public sealed class WorkingDirectoryService
{
    private readonly string _tempRoot;
    private readonly string _outputRoot;

    public WorkingDirectoryService(string tempRoot, string outputRoot)
    {
        _tempRoot = Path.GetFullPath(tempRoot);
        _outputRoot = Path.GetFullPath(outputRoot);

        Directory.CreateDirectory(_tempRoot);
        Directory.CreateDirectory(_outputRoot);
    }

    public WorkingDirectoryContext CreateJobDirectory(string? tempDirOverride, string? outputDirOverride)
    {
        var tempRoot = ResolveOverride(tempDirOverride) ?? _tempRoot;
        var outputRoot = ResolveOverride(outputDirOverride) ?? _outputRoot;

        Directory.CreateDirectory(tempRoot);
        Directory.CreateDirectory(outputRoot);

        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        var jobId = Guid.NewGuid().ToString("N")[..8];
        var jobDir = Path.Combine(tempRoot, $"job-{stamp}-{jobId}");

        var analysisDir = Path.Combine(jobDir, "analysis");
        var framesDir = Path.Combine(jobDir, "frames");
        var logsDir = Path.Combine(jobDir, "logs");

        Directory.CreateDirectory(jobDir);
        Directory.CreateDirectory(analysisDir);
        Directory.CreateDirectory(framesDir);
        Directory.CreateDirectory(logsDir);

        return new WorkingDirectoryContext(jobDir, analysisDir, framesDir, logsDir, outputRoot);
    }

    public void TryCleanup(WorkingDirectoryContext context)
    {
        try
        {
            if (Directory.Exists(context.JobDir))
            {
                Directory.Delete(context.JobDir, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup.
        }
    }

    private static string? ResolveOverride(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return Path.GetFullPath(value);
    }
}

public sealed record WorkingDirectoryContext(
    string JobDir,
    string AnalysisDir,
    string FramesDir,
    string LogsDir,
    string OutputDir);
