using System.Globalization;

namespace YtProducer.Media.Services;

public sealed class AudioProbeService
{
    private readonly string _ffprobePath;
    private readonly FfmpegRunner _runner;

    public AudioProbeService(string ffprobePath, FfmpegRunner runner)
    {
        _ffprobePath = ffprobePath;
        _runner = runner;
    }

    public async Task<double> ProbeDurationAsync(string audioPath, CancellationToken cancellationToken)
    {
        var args = new[]
        {
            "-v", "error",
            "-show_entries", "format=duration",
            "-of", "default=noprint_wrappers=1:nokey=1",
            audioPath
        };

        var result = await _runner.RunAsync(_ffprobePath, args, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"ffprobe failed with code {result.ExitCode}: {result.StdErr}");
        }

        var output = result.StdOut.Trim();
        if (!double.TryParse(output, NumberStyles.Float, CultureInfo.InvariantCulture, out var duration) || duration <= 0)
        {
            throw new InvalidOperationException($"Unable to parse audio duration from ffprobe output: '{output}'");
        }

        return duration;
    }
}
