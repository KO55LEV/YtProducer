using System.Numerics;
using System.Text.Json;
using YtProducer.Media.Models;

namespace YtProducer.Media.Services;

public sealed class AudioAnalysisService
{
    private const int DecodeSampleRate = 44100;
    private const int DecodeChannels = 1;

    private readonly string _ffmpegPath;
    private readonly FfmpegRunner _runner;

    public AudioAnalysisService(string ffmpegPath, FfmpegRunner runner)
    {
        _ffmpegPath = ffmpegPath;
        _runner = runner;
    }

    public async Task<AnalysisDocument> AnalyzeAsync(
        string audioPath,
        double durationSeconds,
        int fps,
        int eqBands,
        string analysisOutputPath,
        CancellationToken cancellationToken)
    {
        var analysisDir = Path.GetDirectoryName(analysisOutputPath) ?? Directory.GetCurrentDirectory();
        Directory.CreateDirectory(analysisDir);

        var pcmPath = Path.Combine(analysisDir, "decoded_mono_44k.pcm");
        var samples = await DecodeToMonoPcmAsync(audioPath, pcmPath, cancellationToken).ConfigureAwait(false);

        var frameCount = Math.Max(1, (int)Math.Ceiling(durationSeconds * fps));
        var hop = DecodeSampleRate / (double)fps;
        var fftSize = MathUtils.NextPowerOfTwo((int)Math.Ceiling(DecodeSampleRate / (double)fps * 2.0));
        fftSize = Math.Max(2048, fftSize);

        var hann = BuildHannWindow(fftSize);

        var rawFrames = new List<float[]>(frameCount);
        var normalizedFrames = new List<AnalysisFrame>(frameCount);
        var energies = new double[frameCount];

        double globalBandMax = 1e-9;

        for (var i = 0; i < frameCount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var centerSample = (int)Math.Round(i * hop);
            var startSample = centerSample - (fftSize / 2);

            var spectrum = BuildSpectrum(samples, startSample, fftSize, hann);
            var bands = CompressToBands(spectrum, DecodeSampleRate, fftSize, eqBands);

            rawFrames.Add(bands);

            for (var b = 0; b < bands.Length; b++)
            {
                if (bands[b] > globalBandMax)
                {
                    globalBandMax = bands[b];
                }
            }
        }

        var norm = globalBandMax <= 1e-8 ? 1.0 : 1.0 / globalBandMax;

        for (var i = 0; i < frameCount; i++)
        {
            var rawBands = rawFrames[i];
            var bands = new float[eqBands];

            for (var b = 0; b < eqBands; b++)
            {
                var v = rawBands[b] * norm * 1.25;
                bands[b] = (float)Math.Clamp(v, 0.0, 1.0);
            }

            var bassEnd = Math.Max(1, (int)Math.Ceiling(eqBands * 0.15));
            var midEnd = Math.Max(bassEnd + 1, (int)Math.Ceiling(eqBands * 0.55));

            var bass = AverageRange(bands, 0, bassEnd);
            var mid = AverageRange(bands, bassEnd, midEnd);
            var high = AverageRange(bands, midEnd, eqBands);
            var energy = (float)Math.Clamp(bass * 0.5 + mid * 0.35 + high * 0.15, 0.0, 1.0);

            energies[i] = energy;

            normalizedFrames.Add(new AnalysisFrame
            {
                I = i,
                T = i / (double)fps,
                Bands = bands,
                Bass = bass,
                Mid = mid,
                High = high,
                Energy = energy,
                Beat = false
            });
        }

        MarkBeats(normalizedFrames, energies, fps);

        var analysis = new AnalysisDocument
        {
            Version = 1,
            DurationSeconds = durationSeconds,
            Fps = fps,
            FrameCount = frameCount,
            SampleRate = DecodeSampleRate,
            Channels = DecodeChannels,
            EqBands = eqBands,
            Frames = normalizedFrames
        };

        var json = JsonSerializer.Serialize(analysis, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        await File.WriteAllTextAsync(analysisOutputPath, json, cancellationToken).ConfigureAwait(false);

        return analysis;
    }

    private async Task<float[]> DecodeToMonoPcmAsync(string audioPath, string pcmPath, CancellationToken cancellationToken)
    {
        var args = new[]
        {
            "-y",
            "-i", audioPath,
            "-vn",
            "-ac", "1",
            "-ar", DecodeSampleRate.ToString(),
            "-f", "f32le",
            pcmPath
        };

        var result = await _runner.RunAsync(_ffmpegPath, args, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"ffmpeg audio decode failed: {result.StdErr}");
        }

        var bytes = await File.ReadAllBytesAsync(pcmPath, cancellationToken).ConfigureAwait(false);
        var samples = new float[bytes.Length / sizeof(float)];
        Buffer.BlockCopy(bytes, 0, samples, 0, bytes.Length);

        return samples;
    }

    private static double[] BuildSpectrum(float[] samples, int startSample, int fftSize, float[] hann)
    {
        var buffer = new Complex[fftSize];

        for (var i = 0; i < fftSize; i++)
        {
            var sampleIndex = startSample + i;
            var sample = 0.0;

            if ((uint)sampleIndex < (uint)samples.Length)
            {
                sample = samples[sampleIndex];
            }

            buffer[i] = new Complex(sample * hann[i], 0.0);
        }

        FftInPlace(buffer);

        var half = fftSize / 2;
        var magnitudes = new double[half];

        for (var i = 0; i < half; i++)
        {
            var mag = buffer[i].Magnitude / half;
            magnitudes[i] = mag;
        }

        return magnitudes;
    }

    private static float[] CompressToBands(double[] magnitudes, int sampleRate, int fftSize, int eqBands)
    {
        var result = new float[eqBands];
        var nyquist = sampleRate / 2.0;
        var binCount = magnitudes.Length;

        for (var b = 0; b < eqBands; b++)
        {
            var startFreq = 20.0 * Math.Pow(nyquist / 20.0, b / (double)eqBands);
            var endFreq = 20.0 * Math.Pow(nyquist / 20.0, (b + 1) / (double)eqBands);

            var startBin = Math.Clamp((int)Math.Floor(startFreq / nyquist * binCount), 1, binCount - 1);
            var endBin = Math.Clamp((int)Math.Ceiling(endFreq / nyquist * binCount), startBin + 1, binCount);

            double sum = 0;
            for (var i = startBin; i < endBin; i++)
            {
                sum += magnitudes[i];
            }

            var avg = sum / (endBin - startBin);
            var compressed = Math.Log10(1.0 + avg * 35.0);
            result[b] = (float)compressed;
        }

        return result;
    }

    private static float AverageRange(float[] values, int startInclusive, int endExclusive)
    {
        startInclusive = Math.Clamp(startInclusive, 0, values.Length);
        endExclusive = Math.Clamp(endExclusive, startInclusive + 1, values.Length);

        double sum = 0;
        for (var i = startInclusive; i < endExclusive; i++)
        {
            sum += values[i];
        }

        return (float)(sum / (endExclusive - startInclusive));
    }

    private static void MarkBeats(IReadOnlyList<AnalysisFrame> frames, IReadOnlyList<double> energies, int fps)
    {
        var lookback = Math.Max(6, fps);
        var cooldown = Math.Max(2, fps / 8);
        var lastBeat = -cooldown;

        for (var i = 0; i < frames.Count; i++)
        {
            var start = Math.Max(0, i - lookback);
            double movingSum = 0;
            var count = 0;

            for (var j = start; j < i; j++)
            {
                movingSum += energies[j];
                count++;
            }

            var movingAverage = count > 0 ? movingSum / count : energies[i];

            var isLocalPeak = i > 0 && i < frames.Count - 1
                              && energies[i] > energies[i - 1]
                              && energies[i] >= energies[i + 1];

            var threshold = movingAverage * 1.35 + 0.015;
            var beat = isLocalPeak && energies[i] > threshold && i - lastBeat >= cooldown;

            frames[i].Beat = beat;

            if (beat)
            {
                lastBeat = i;
            }
        }
    }

    private static float[] BuildHannWindow(int size)
    {
        var window = new float[size];
        if (size == 1)
        {
            window[0] = 1f;
            return window;
        }

        for (var i = 0; i < size; i++)
        {
            window[i] = (float)(0.5 * (1.0 - Math.Cos((2.0 * Math.PI * i) / (size - 1))));
        }

        return window;
    }

    private static void FftInPlace(Complex[] buffer)
    {
        var n = buffer.Length;
        var bits = BitOperations.Log2((uint)n);

        for (var i = 0; i < n; i++)
        {
            var j = (int)ReverseBits((uint)i, bits);
            if (j <= i)
            {
                continue;
            }

            (buffer[i], buffer[j]) = (buffer[j], buffer[i]);
        }

        for (var len = 2; len <= n; len <<= 1)
        {
            var angle = -2.0 * Math.PI / len;
            var wLen = new Complex(Math.Cos(angle), Math.Sin(angle));

            for (var i = 0; i < n; i += len)
            {
                var w = Complex.One;
                var half = len / 2;

                for (var j = 0; j < half; j++)
                {
                    var u = buffer[i + j];
                    var v = buffer[i + j + half] * w;

                    buffer[i + j] = u + v;
                    buffer[i + j + half] = u - v;
                    w *= wLen;
                }
            }
        }
    }

    private static uint ReverseBits(uint value, int bitCount)
    {
        var result = 0u;
        for (var i = 0; i < bitCount; i++)
        {
            result = (result << 1) | (value & 1u);
            value >>= 1;
        }

        return result;
    }
}
