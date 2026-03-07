using YtProducer.Media.Models;

namespace YtProducer.Media.Services;

public sealed class FrameRenderService
{
    private readonly string _ffmpegPath;
    private readonly string _ffprobePath;
    private readonly FfmpegRunner _runner;

    public FrameRenderService(string ffmpegPath, string ffprobePath, FfmpegRunner runner)
    {
        _ffmpegPath = ffmpegPath;
        _ffprobePath = ffprobePath;
        _runner = runner;
    }

    public async Task RenderFramesAsync(
        string imagePath,
        AnalysisDocument analysis,
        string framesDir,
        int width,
        int height,
        int seed,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(framesDir);

        await RenderFramesCoreAsync(
            imagePath,
            analysis,
            width,
            height,
            seed,
            async (frameIndex, framePixels) =>
            {
                var framePath = Path.Combine(framesDir, $"frame_{frameIndex + 1:000000}.png");
                ImageUtils.SaveRgbaAsPng(framePath, width, height, framePixels);
                await Task.CompletedTask;
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task RenderFramesToRawStreamAsync(
        string imagePath,
        AnalysisDocument analysis,
        int width,
        int height,
        int seed,
        Stream output,
        CancellationToken cancellationToken)
    {
        await RenderFramesCoreAsync(
            imagePath,
            analysis,
            width,
            height,
            seed,
            async (_, framePixels) =>
            {
                await output.WriteAsync(framePixels, cancellationToken).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);
    }

    private async Task RenderFramesCoreAsync(
        string imagePath,
        AnalysisDocument analysis,
        int width,
        int height,
        int seed,
        Func<int, byte[], Task> onFrameAsync,
        CancellationToken cancellationToken)
    {

        var sourceImage = await ImageUtils
            .LoadImageAsync(_ffmpegPath, _ffprobePath, _runner, imagePath, cancellationToken)
            .ConfigureAwait(false);

        var rng = new DeterministicRandom(seed);
        var particles = CreateParticles(rng, Math.Clamp(140 + analysis.EqBands, 100, 300));
        var smoothedBars = new double[analysis.EqBands];
        var framePixels = new byte[checked(width * height * 4)];

        double beatPunch = 0;

        for (var frameIndex = 0; frameIndex < analysis.FrameCount; frameIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var frame = analysis.Frames[frameIndex];
            if (frame.Beat)
            {
                beatPunch = 1.0;
            }

            FillBackground(framePixels, width, height, frame.Energy, frame.High);

            var progress = analysis.FrameCount <= 1 ? 0.0 : frameIndex / (double)(analysis.FrameCount - 1);
            var baseZoom = 1.0 + 0.08 * progress;
            var pulseZoom = 1.0 + beatPunch * 0.045 + frame.Energy * 0.01;
            var zoom = baseZoom * pulseZoom;

            var rotation = Math.Sin(frame.T * 0.21) * 1.35 + Math.Cos(frame.T * 0.11) * 0.6;
            var driftX = Math.Sin(frame.T * 0.13) * width * 0.015;
            var driftY = Math.Cos(frame.T * 0.09) * height * 0.012;

            var shakeX = (MathUtils.HashToSignedUnit(seed, frameIndex, 17) * width * 0.0045) * beatPunch;
            var shakeY = (MathUtils.HashToSignedUnit(seed, frameIndex, 29) * height * 0.0060) * beatPunch;

            DrawImageLayer(
                framePixels,
                width,
                height,
                sourceImage,
                zoom * 1.10,
                rotation * 0.8,
                driftX * 1.2 + shakeX,
                driftY * 1.2 + shakeY,
                alpha: 0.25 + frame.Energy * 0.25,
                additive: true,
                blur: true,
                tintR: 0.80,
                tintG: 0.90,
                tintB: 1.05);

            DrawImageLayer(
                framePixels,
                width,
                height,
                sourceImage,
                zoom,
                rotation,
                driftX + shakeX,
                driftY + shakeY,
                alpha: 1.0,
                additive: false,
                blur: false,
                tintR: 1.0,
                tintG: 1.0,
                tintB: 1.0);

            DrawParticles(framePixels, width, height, particles, frame, frameIndex, seed);
            DrawEqualizer(framePixels, width, height, frame, smoothedBars);
            ApplyVignetteAndGrain(framePixels, width, height, frameIndex, seed);
            await onFrameAsync(frameIndex, framePixels).ConfigureAwait(false);

            beatPunch *= 0.82;
        }
    }

    private static IReadOnlyList<Particle> CreateParticles(DeterministicRandom rng, int count)
    {
        var particles = new List<Particle>(count);
        for (var i = 0; i < count; i++)
        {
            particles.Add(new Particle
            {
                X = rng.NextFloat(),
                Y = rng.NextFloat(),
                Vx = rng.NextFloat(-0.014f, 0.014f),
                Vy = rng.NextFloat(-0.020f, -0.004f),
                Radius = rng.NextFloat(0.8f, 3.3f),
                Alpha = rng.NextFloat(0.05f, 0.25f),
                Phase = rng.NextFloat(0f, MathF.Tau)
            });
        }

        return particles;
    }

    private static void FillBackground(byte[] buffer, int width, int height, float energy, float high)
    {
        var cx = width * 0.5;
        var cy = height * 0.45;
        var maxDist = Math.Sqrt(cx * cx + cy * cy);

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var dx = x - cx;
                var dy = y - cy;
                var dist = Math.Sqrt(dx * dx + dy * dy) / maxDist;
                var glow = Math.Max(0.0, 1.0 - Math.Pow(dist, 1.3));

                var r = 8 + (int)(glow * (12 + energy * 18));
                var g = 10 + (int)(glow * (20 + high * 22));
                var b = 14 + (int)(glow * (36 + energy * 28));

                var index = (y * width + x) * 4;
                buffer[index] = (byte)Math.Clamp(r, 0, 255);
                buffer[index + 1] = (byte)Math.Clamp(g, 0, 255);
                buffer[index + 2] = (byte)Math.Clamp(b, 0, 255);
                buffer[index + 3] = 255;
            }
        }
    }

    private static void DrawImageLayer(
        byte[] destination,
        int width,
        int height,
        RgbaImage source,
        double zoom,
        double rotationDeg,
        double offsetX,
        double offsetY,
        double alpha,
        bool additive,
        bool blur,
        double tintR,
        double tintG,
        double tintB)
    {
        var coverScale = ImageUtils.CalculateCoverScale(source.Width, source.Height, width, height);
        var scale = coverScale * zoom;
        var invScale = 1.0 / scale;

        var radians = rotationDeg * (Math.PI / 180.0);
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);

        var halfW = width * 0.5;
        var halfH = height * 0.5;
        var srcHalfW = source.Width * 0.5;
        var srcHalfH = source.Height * 0.5;

        for (var y = 0; y < height; y++)
        {
            var dy = y - halfH - offsetY;

            for (var x = 0; x < width; x++)
            {
                var dx = x - halfW - offsetX;

                var localX = (dx * cos + dy * sin) * invScale;
                var localY = (-dx * sin + dy * cos) * invScale;

                var sx = localX + srcHalfW;
                var sy = localY + srcHalfH;

                if (sx < 0 || sy < 0 || sx >= source.Width - 1 || sy >= source.Height - 1)
                {
                    continue;
                }

                SampleBilinear(source, sx, sy, blur, out var sr, out var sg, out var sb, out var sa);

                sr *= tintR;
                sg *= tintG;
                sb *= tintB;

                BlendPixel(destination, width, x, y, sr, sg, sb, sa * alpha, additive);
            }
        }
    }

    private static void SampleBilinear(
        RgbaImage image,
        double x,
        double y,
        bool blur,
        out double r,
        out double g,
        out double b,
        out double a)
    {
        if (!blur)
        {
            Sample(image, x, y, out r, out g, out b, out a);
            return;
        }

        Sample(image, x, y, out var r0, out var g0, out var b0, out var a0);
        Sample(image, x - 1.6, y, out var r1, out var g1, out var b1, out var a1);
        Sample(image, x + 1.6, y, out var r2, out var g2, out var b2, out var a2);
        Sample(image, x, y - 1.6, out var r3, out var g3, out var b3, out var a3);
        Sample(image, x, y + 1.6, out var r4, out var g4, out var b4, out var a4);

        r = (r0 * 0.45) + (r1 + r2 + r3 + r4) * 0.1375;
        g = (g0 * 0.45) + (g1 + g2 + g3 + g4) * 0.1375;
        b = (b0 * 0.45) + (b1 + b2 + b3 + b4) * 0.1375;
        a = (a0 * 0.45) + (a1 + a2 + a3 + a4) * 0.1375;
    }

    private static void Sample(
        RgbaImage image,
        double x,
        double y,
        out double r,
        out double g,
        out double b,
        out double a)
    {
        if (x < 0 || y < 0 || x >= image.Width - 1 || y >= image.Height - 1)
        {
            r = g = b = a = 0;
            return;
        }

        var x0 = (int)x;
        var y0 = (int)y;
        var x1 = x0 + 1;
        var y1 = y0 + 1;

        var tx = x - x0;
        var ty = y - y0;

        ReadPixel(image, x0, y0, out var r00, out var g00, out var b00, out var a00);
        ReadPixel(image, x1, y0, out var r10, out var g10, out var b10, out var a10);
        ReadPixel(image, x0, y1, out var r01, out var g01, out var b01, out var a01);
        ReadPixel(image, x1, y1, out var r11, out var g11, out var b11, out var a11);

        r = Lerp(Lerp(r00, r10, tx), Lerp(r01, r11, tx), ty);
        g = Lerp(Lerp(g00, g10, tx), Lerp(g01, g11, tx), ty);
        b = Lerp(Lerp(b00, b10, tx), Lerp(b01, b11, tx), ty);
        a = Lerp(Lerp(a00, a10, tx), Lerp(a01, a11, tx), ty);
    }

    private static void ReadPixel(RgbaImage image, int x, int y, out double r, out double g, out double b, out double a)
    {
        var index = (y * image.Width + x) * 4;
        r = image.Pixels[index] / 255.0;
        g = image.Pixels[index + 1] / 255.0;
        b = image.Pixels[index + 2] / 255.0;
        a = image.Pixels[index + 3] / 255.0;
    }

    private static void BlendPixel(byte[] buffer, int width, int x, int y, double r, double g, double b, double alpha, bool additive)
    {
        if (alpha <= 0)
        {
            return;
        }

        var index = (y * width + x) * 4;
        var dr = buffer[index] / 255.0;
        var dg = buffer[index + 1] / 255.0;
        var db = buffer[index + 2] / 255.0;

        if (additive)
        {
            dr = Math.Clamp(dr + r * alpha, 0.0, 1.0);
            dg = Math.Clamp(dg + g * alpha, 0.0, 1.0);
            db = Math.Clamp(db + b * alpha, 0.0, 1.0);
        }
        else
        {
            var fa = Math.Clamp(alpha, 0.0, 1.0);
            dr = dr * (1.0 - fa) + r * fa;
            dg = dg * (1.0 - fa) + g * fa;
            db = db * (1.0 - fa) + b * fa;
        }

        buffer[index] = (byte)(dr * 255.0 + 0.5);
        buffer[index + 1] = (byte)(dg * 255.0 + 0.5);
        buffer[index + 2] = (byte)(db * 255.0 + 0.5);
        buffer[index + 3] = 255;
    }

    private static void DrawParticles(
        byte[] buffer,
        int width,
        int height,
        IReadOnlyList<Particle> particles,
        AnalysisFrame frame,
        int frameIndex,
        int seed)
    {
        for (var i = 0; i < particles.Count; i++)
        {
            var p = particles[i];
            var tx = MathUtils.Repeat01(p.X + p.Vx * (float)frame.T + MathF.Sin((float)frame.T * 0.2f + p.Phase) * 0.01f);
            var ty = MathUtils.Repeat01(p.Y + p.Vy * (float)frame.T + MathF.Cos((float)frame.T * 0.13f + p.Phase) * 0.006f);

            var x = tx * width;
            var y = ty * height;

            var pulse = 0.6f + 0.4f * MathF.Sin((float)frame.T * 0.7f + p.Phase);
            var alpha = Math.Clamp((p.Alpha + (float)frame.High * 0.18f + (float)frame.Energy * 0.10f) * pulse, 0f, 0.9f);
            var radius = p.Radius * (1f + (float)frame.High * 0.5f);

            var hueBias = MathUtils.HashToUnit(seed + 303, frameIndex, i);
            var color = hueBias < 0.5f
                ? (r: 0.70, g: 0.90, b: 1.0)
                : (r: 1.0, g: 0.78, b: 0.94);

            DrawSoftCircleAdd(buffer, width, height, x, y, radius, color.r, color.g, color.b, alpha);
        }
    }

    private static void DrawSoftCircleAdd(
        byte[] buffer,
        int width,
        int height,
        float cx,
        float cy,
        float radius,
        double r,
        double g,
        double b,
        float alpha)
    {
        var minX = Math.Max(0, (int)Math.Floor(cx - radius - 1));
        var maxX = Math.Min(width - 1, (int)Math.Ceiling(cx + radius + 1));
        var minY = Math.Max(0, (int)Math.Floor(cy - radius - 1));
        var maxY = Math.Min(height - 1, (int)Math.Ceiling(cy + radius + 1));

        var radiusSq = radius * radius;
        if (radiusSq <= 0f)
        {
            return;
        }

        for (var y = minY; y <= maxY; y++)
        {
            var dy = y - cy;
            for (var x = minX; x <= maxX; x++)
            {
                var dx = x - cx;
                var distSq = dx * dx + dy * dy;
                if (distSq > radiusSq)
                {
                    continue;
                }

                var dist = MathF.Sqrt(distSq) / radius;
                var falloff = (1f - dist);
                var pixelAlpha = alpha * falloff * falloff;
                BlendPixel(buffer, width, x, y, r, g, b, pixelAlpha, additive: true);
            }
        }
    }

    private static void DrawEqualizer(byte[] buffer, int width, int height, AnalysisFrame frame, double[] smoothed)
    {
        var bandCount = frame.Bands.Length;
        if (bandCount == 0)
        {
            return;
        }

        var areaWidth = width * 0.78;
        var gap = 2.0;
        var barWidth = Math.Max(2.0, (areaWidth - (bandCount - 1) * gap) / bandCount);
        var totalWidth = barWidth * bandCount + gap * (bandCount - 1);
        var startX = (width - totalWidth) * 0.5;
        var baseline = height * 0.86;
        var maxHeight = height * 0.34;

        for (var i = 0; i < bandCount; i++)
        {
            var target = frame.Bands[i];
            var attack = target > smoothed[i] ? 0.72 : 0.18;
            smoothed[i] = smoothed[i] + (target - smoothed[i]) * attack;

            var bassWeight = 1.0 + (1.0 - i / Math.Max(1.0, bandCount - 1.0)) * 0.45 * frame.Bass;
            var visual = Math.Pow(smoothed[i], 0.85) * bassWeight;
            var h = Math.Max(2.0, visual * maxHeight);

            var x = startX + i * (barWidth + gap);
            var y = baseline - h;

            var isCyan = i % 2 == 0;
            var glowColor = isCyan ? (r: 0.42, g: 0.92, b: 1.0) : (r: 1.0, g: 0.55, b: 0.88);

            DrawRectAdd(
                buffer,
                width,
                height,
                x - 1.5,
                y - 2.0,
                barWidth + 3.0,
                h + 4.0,
                glowColor.r,
                glowColor.g,
                glowColor.b,
                alpha: 0.20);

            DrawRectAlpha(
                buffer,
                width,
                height,
                x,
                y,
                barWidth,
                h,
                r: 0.95,
                g: 0.97,
                b: 1.0,
                alpha: 0.92);
        }
    }

    private static void DrawRectAdd(
        byte[] buffer,
        int width,
        int height,
        double x,
        double y,
        double w,
        double h,
        double r,
        double g,
        double b,
        double alpha)
    {
        var startX = Math.Max(0, (int)Math.Floor(x));
        var endX = Math.Min(width - 1, (int)Math.Ceiling(x + w));
        var startY = Math.Max(0, (int)Math.Floor(y));
        var endY = Math.Min(height - 1, (int)Math.Ceiling(y + h));

        for (var py = startY; py <= endY; py++)
        {
            for (var px = startX; px <= endX; px++)
            {
                BlendPixel(buffer, width, px, py, r, g, b, alpha, additive: true);
            }
        }
    }

    private static void DrawRectAlpha(
        byte[] buffer,
        int width,
        int height,
        double x,
        double y,
        double w,
        double h,
        double r,
        double g,
        double b,
        double alpha)
    {
        var startX = Math.Max(0, (int)Math.Floor(x));
        var endX = Math.Min(width - 1, (int)Math.Ceiling(x + w));
        var startY = Math.Max(0, (int)Math.Floor(y));
        var endY = Math.Min(height - 1, (int)Math.Ceiling(y + h));

        for (var py = startY; py <= endY; py++)
        {
            for (var px = startX; px <= endX; px++)
            {
                BlendPixel(buffer, width, px, py, r, g, b, alpha, additive: false);
            }
        }
    }

    private static void ApplyVignetteAndGrain(byte[] buffer, int width, int height, int frameIndex, int seed)
    {
        var cx = width * 0.5;
        var cy = height * 0.5;
        var maxDist = Math.Sqrt(cx * cx + cy * cy);

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var index = (y * width + x) * 4;

                var dx = x - cx;
                var dy = y - cy;
                var nd = Math.Sqrt(dx * dx + dy * dy) / maxDist;
                var vignette = 1.0 - Math.Pow(Math.Max(0.0, (nd - 0.55) / 0.45), 1.6) * 0.55;
                vignette = Math.Clamp(vignette, 0.35, 1.0);

                var grain = MathUtils.HashToSignedUnit(seed + 1723 + frameIndex, x, y) * 0.045;

                var r = buffer[index] / 255.0;
                var g = buffer[index + 1] / 255.0;
                var b = buffer[index + 2] / 255.0;

                r = Math.Clamp(r * vignette + grain, 0.0, 1.0);
                g = Math.Clamp(g * vignette + grain, 0.0, 1.0);
                b = Math.Clamp(b * vignette + grain, 0.0, 1.0);

                buffer[index] = (byte)(r * 255.0 + 0.5);
                buffer[index + 1] = (byte)(g * 255.0 + 0.5);
                buffer[index + 2] = (byte)(b * 255.0 + 0.5);
            }
        }
    }

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;

    private sealed class Particle
    {
        public float X { get; init; }

        public float Y { get; init; }

        public float Vx { get; init; }

        public float Vy { get; init; }

        public float Radius { get; init; }

        public float Alpha { get; init; }

        public float Phase { get; init; }
    }
}
