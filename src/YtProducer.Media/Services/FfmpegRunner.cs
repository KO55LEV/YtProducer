using System.Diagnostics;
using System.Text;

namespace YtProducer.Media.Services;

public sealed class FfmpegRunner
{
    public async Task<FfmpegRunResult> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        string? workingDirectory = null,
        string? stderrFilePath = null,
        CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory ?? Directory.GetCurrentDirectory()
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };

        var stdoutBuilder = new StringBuilder();
        var stderrBuilder = new StringBuilder();

        StreamWriter? stderrWriter = null;
        if (!string.IsNullOrWhiteSpace(stderrFilePath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(stderrFilePath!)!);
            stderrWriter = new StreamWriter(stderrFilePath!, append: false, Encoding.UTF8);
        }

        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException($"Failed to start process: {executable}");
            }

            var stdoutTask = ReadPipeAsync(process.StandardOutput, stdoutBuilder, null, cancellationToken);
            var stderrTask = ReadPipeAsync(process.StandardError, stderrBuilder, stderrWriter, cancellationToken);

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);

            return new FfmpegRunResult(
                process.ExitCode,
                stdoutBuilder.ToString(),
                stderrBuilder.ToString(),
                BuildCommandLine(executable, arguments));
        }
        finally
        {
            if (stderrWriter is not null)
            {
                await stderrWriter.FlushAsync(cancellationToken).ConfigureAwait(false);
                await stderrWriter.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    public async Task<FfmpegBinaryRunResult> RunBinaryAsync(
        string executable,
        IReadOnlyList<string> arguments,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory ?? Directory.GetCurrentDirectory()
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException($"Failed to start process: {executable}");
        }

        using var stdoutMemory = new MemoryStream();

        var stdoutTask = process.StandardOutput.BaseStream.CopyToAsync(stdoutMemory, cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);

        return new FfmpegBinaryRunResult(
            process.ExitCode,
            stdoutMemory.ToArray(),
            stderr,
            BuildCommandLine(executable, arguments));
    }

    private static async Task ReadPipeAsync(
        StreamReader reader,
        StringBuilder output,
        StreamWriter? mirror,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                break;
            }

            output.AppendLine(line);
            if (mirror is not null)
            {
                await mirror.WriteLineAsync(line.AsMemory(), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public static string BuildCommandLine(string executable, IReadOnlyList<string> arguments)
    {
        static string Quote(string value)
        {
            if (value.Length == 0)
            {
                return "\"\"";
            }

            if (value.IndexOfAny([' ', '\t', '"']) < 0)
            {
                return value;
            }

            return '"' + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + '"';
        }

        var parts = new List<string>(arguments.Count + 1) { Quote(executable) };
        parts.AddRange(arguments.Select(Quote));
        return string.Join(' ', parts);
    }
}

public sealed record FfmpegRunResult(int ExitCode, string StdOut, string StdErr, string CommandLine);

public sealed record FfmpegBinaryRunResult(int ExitCode, byte[] StdOutBytes, string StdErr, string CommandLine);
