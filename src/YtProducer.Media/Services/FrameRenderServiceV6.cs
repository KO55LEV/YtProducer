using YtProducer.Media.Models;

namespace YtProducer.Media.Services;

public sealed class FrameRenderServiceV6
{
    private readonly string _ffmpegPath;
    private readonly string _ffprobePath;
    private readonly FfmpegRunner _runner;

    public FrameRenderServiceV6(string ffmpegPath, string ffprobePath, FfmpegRunner runner)
    {
        _ffmpegPath = ffmpegPath;
        _ffprobePath = ffprobePath;
        _runner = runner;
    }

    public async Task RenderFramesToRawStreamAsync(
        string imagePath,
        string logoPath,
        AnalysisDocument analysis,
        int width,
        int height,
        int seed,
        Stream output,
        Func<int, int, Task>? onProgressAsync,
        CancellationToken cancellationToken)
    {
        var sourceImage = await ImageUtils
            .LoadImageAsync(_ffmpegPath, _ffprobePath, _runner, imagePath, cancellationToken)
            .ConfigureAwait(false);

        var logoImage = await ImageUtils
            .LoadImageAsync(_ffmpegPath, _ffprobePath, _runner, logoPath, cancellationToken)
            .ConfigureAwait(false);

        var rng = new DeterministicRandom(seed);
        var dust = CreateDust(rng, Math.Clamp((width * height) / 3600, 320, 1100));
        var smoothedBands = new float[analysis.EqBands];
        var framePixels = new byte[checked(width * height * 4)];
        var beatPunch = 0.0;
        var dustSpeed = 0.6f;

        for (var frameIndex = 0; frameIndex < analysis.FrameCount; frameIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var frame = analysis.Frames[frameIndex];
            if (frame.Beat)
            {
                beatPunch = 1.0;
            }

            FillBackground(framePixels, width, height, frame, frameIndex, seed);

            var progress = analysis.FrameCount <= 1 ? 0.0 : frameIndex / (double)(analysis.FrameCount - 1);
            var baseZoom = 1.0 + 0.045 * progress;
            var pulseZoom = 1.0 + beatPunch * 0.05 + frame.Energy * 0.014;
            var zoom = baseZoom * pulseZoom;

            var rotation = Math.Sin(frame.T * 0.15) * 1.25 + Math.Cos(frame.T * 0.06) * 0.65;
            var driftX = Math.Sin(frame.T * 0.10) * width * 0.016;
            var driftY = Math.Cos(frame.T * 0.075) * height * 0.011;
            var shakeX = MathUtils.HashToSignedUnit(seed, frameIndex, 13) * width * 0.0055 * beatPunch;
            var shakeY = MathUtils.HashToSignedUnit(seed, frameIndex, 27) * height * 0.0065 * beatPunch;

            DrawImageLayer(
                framePixels, width, height, sourceImage,
                zoom * 1.18, rotation * 0.45,
                driftX * 1.6 + shakeX * 0.4, driftY * 1.5 + shakeY * 0.4,
                0.24 + frame.High * 0.20, true, 0.72, 0.86, 1.08);

            DrawImageLayer(
                framePixels, width, height, sourceImage,
                zoom * 1.06, rotation * 0.70,
                driftX * 1.25 + shakeX * 0.7, driftY * 1.15 + shakeY * 0.7,
                0.40 + frame.Mid * 0.22, true, 0.88, 0.95, 1.02);

            DrawImageLayer(
                framePixels, width, height, sourceImage,
                zoom, rotation,
                driftX + shakeX, driftY + shakeY,
                1.0, false, 1.0, 1.0, 1.0);

            var targetDustSpeed = 0.40f + frame.Energy * 0.70f + frame.Bass * 0.55f + (frame.Beat ? 0.55f : 0f);
            dustSpeed = dustSpeed * 0.90f + targetDustSpeed * 0.10f;
            dustSpeed = Math.Clamp(dustSpeed, 0.35f, 1.75f);

            DrawDust(framePixels, width, height, dust, frame, dustSpeed, seed);
            DrawEqualizer(framePixels, width, height, frame, smoothedBands);
            DrawLogo(framePixels, width, height, logoImage, frame);

            await output.WriteAsync(framePixels, cancellationToken).ConfigureAwait(false);
            if (onProgressAsync is not null)
            {
                await onProgressAsync(frameIndex + 1, analysis.FrameCount).ConfigureAwait(false);
            }

            beatPunch *= 0.80;
        }
    }

    private static void DrawLogo(byte[] buffer, int width, int height, RgbaImage logo, AnalysisFrame frame)
    {
        var targetWidth = Math.Max(120, (int)Math.Round(width * 0.135));
        var baseScale = targetWidth / (double)logo.Width;
        var pulseScale = 1.0 + (frame.Beat ? 0.045 : 0.0) + frame.Energy * 0.020;
        var scale = baseScale * pulseScale;
        var scaledWidth = Math.Max(120, (int)Math.Round(logo.Width * scale));
        var targetHeight = Math.Max(40, (int)Math.Round(logo.Height * scale));
        var margin = Math.Max(18, (int)Math.Round(width * 0.028));

        // Top-left anchor with subtle micro-float motion.
        var floatX = (int)Math.Round(Math.Sin(frame.T * 0.90) * 2.0);
        var floatY = (int)Math.Round(Math.Cos(frame.T * 0.75) * 2.0);
        var x = margin + floatX;
        var y = margin + floatY;

        // Energy/beat breathing.
        var opacity = Math.Clamp(0.76 + frame.Energy * 0.10 + (frame.Beat ? 0.08 : 0.0), 0.70, 0.94);

        // Beat-reactive glow for eye-catching branding.
        var glowStrength = Math.Clamp(0.18 + frame.Energy * 0.20 + (frame.Beat ? 0.20 : 0.0), 0.15, 0.42);
        DrawResizedImageAdditive(
            buffer,
            width,
            height,
            logo,
            x - (int)Math.Round(scaledWidth * 0.035),
            y - (int)Math.Round(targetHeight * 0.035),
            (int)Math.Round(scaledWidth * 1.07),
            (int)Math.Round(targetHeight * 1.07),
            glowStrength);

        DrawResizedImage(buffer, width, height, logo, x, y, scaledWidth, targetHeight, opacity);
    }

    private static void DrawResizedImage(
        byte[] destination,
        int width,
        int height,
        RgbaImage source,
        int dstX,
        int dstY,
        int dstW,
        int dstH,
        double opacity)
    {
        var invW = source.Width / (double)dstW;
        var invH = source.Height / (double)dstH;

        for (var y = 0; y < dstH; y++)
        {
            var py = dstY + y;
            if (py < 0 || py >= height) continue;
            var sy = y * invH;

            for (var x = 0; x < dstW; x++)
            {
                var px = dstX + x;
                if (px < 0 || px >= width) continue;
                var sx = x * invW;

                Sample(source, sx, sy, out var sr, out var sg, out var sb, out var sa);
                if (sa <= 0) continue;

                BlendPixel(destination, width, px, py, sr, sg, sb, sa * opacity, false);
            }
        }
    }

    private static void DrawResizedImageAdditive(
        byte[] destination,
        int width,
        int height,
        RgbaImage source,
        int dstX,
        int dstY,
        int dstW,
        int dstH,
        double opacity)
    {
        var invW = source.Width / (double)dstW;
        var invH = source.Height / (double)dstH;

        for (var y = 0; y < dstH; y++)
        {
            var py = dstY + y;
            if (py < 0 || py >= height) continue;
            var sy = y * invH;

            for (var x = 0; x < dstW; x++)
            {
                var px = dstX + x;
                if (px < 0 || px >= width) continue;
                var sx = x * invW;

                Sample(source, sx, sy, out var sr, out var sg, out var sb, out var sa);
                if (sa <= 0) continue;

                // Warm/cool mixed glow tint to match logo palette.
                var gr = sr * 1.06;
                var gg = sg * 1.03;
                var gb = sb * 1.10;
                BlendPixel(destination, width, px, py, gr, gg, gb, sa * opacity, true);
            }
        }
    }

    private static List<DustParticle> CreateDust(DeterministicRandom rng, int count)
    {
        var list = new List<DustParticle>(count);
        for (var i = 0; i < count; i++)
        {
            list.Add(new DustParticle
            {
                X = rng.NextFloat(),
                Y = rng.NextFloat(),
                Z = rng.NextFloat(0.15f, 1.0f),
                Vx = rng.NextFloat(-0.18f, 0.18f),
                Vy = rng.NextFloat(-0.26f, -0.05f),
                Radius = rng.NextFloat(0.7f, 3.4f),
                Alpha = rng.NextFloat(0.05f, 0.28f),
                Flicker = rng.NextFloat(0f, MathF.Tau)
            });
        }

        return list;
    }

    private static void FillBackground(byte[] buffer, int width, int height, AnalysisFrame frame, int frameIndex, int seed)
    {
        var cx = width * 0.5;
        var cy = height * 0.46;
        var maxDist = Math.Sqrt(cx * cx + cy * cy);
        var hazeShift = Math.Sin(frame.T * 0.35 + seed * 0.001) * width * 0.08;

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var dx = x - cx;
                var dy = y - cy;
                var dist = Math.Sqrt(dx * dx + dy * dy) / maxDist;
                var glow = Math.Max(0.0, 1.0 - Math.Pow(dist, 1.25));
                var shaft = Math.Max(0.0, 1.0 - Math.Abs((x - hazeShift) - cx) / (width * 0.34));
                var pulse = 0.65 + frame.Energy * 0.35 + Math.Sin(frameIndex * 0.11 + x * 0.0022) * 0.08;

                var r = 6 + (int)((glow * 25 + shaft * 10) * pulse);
                var g = 9 + (int)((glow * 32 + shaft * 16) * (0.95 + frame.Mid * 0.25));
                var b = 14 + (int)((glow * 45 + shaft * 21) * (0.95 + frame.High * 0.35));

                var idx = (y * width + x) * 4;
                buffer[idx] = (byte)Math.Clamp(r, 0, 255);
                buffer[idx + 1] = (byte)Math.Clamp(g, 0, 255);
                buffer[idx + 2] = (byte)Math.Clamp(b, 0, 255);
                buffer[idx + 3] = 255;
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

                Sample(source, sx, sy, out var sr, out var sg, out var sb, out var sa);
                BlendPixel(destination, width, x, y, sr * tintR, sg * tintG, sb * tintB, sa * alpha, additive);
            }
        }
    }

    private static void DrawDust(
        byte[] buffer,
        int width,
        int height,
        IReadOnlyList<DustParticle> particles,
        AnalysisFrame frame,
        float speedFactor,
        int seed)
    {
        var time = (float)frame.T;

        for (var i = 0; i < particles.Count; i++)
        {
            var p = particles[i];
            var depth = p.Z;

            var depthSlowdown = 0.35f + depth * 0.95f;
            var beatGust = frame.Beat ? 1.35f : 1.0f;
            var localSpeed = speedFactor * depthSlowdown * beatGust;

            var driftX = (float)(Math.Sin(frame.T * (0.65 + depth * 0.7) + p.Flicker) * 0.0022);
            var driftY = (float)(Math.Cos(frame.T * (0.50 + depth * 0.55) + p.Flicker * 0.85) * 0.0016);
            var x = p.X + p.Vx * localSpeed * time + driftX;
            var y = p.Y + p.Vy * localSpeed * time + driftY;

            x -= MathF.Floor(x);
            y -= MathF.Floor(y);

            var px = x * width;
            var py = y * height;
            var radius = p.Radius * (0.42f + depth * 1.7f) * (0.95f + frame.Energy * 0.45f);
            var alpha = p.Alpha * (0.35f + depth * 0.9f) * (0.85f + frame.High * 0.6f);
            alpha *= 0.75f + 0.25f * (float)Math.Sin(frame.T * 1.7 + p.Flicker + seed * 0.001);

            DrawSoftCircle(buffer, width, height, px, py, radius, alpha);
        }
    }

    private static void DrawSoftCircle(byte[] buffer, int width, int height, float cx, float cy, float radius, float alpha)
    {
        var minX = Math.Max(0, (int)Math.Floor(cx - radius * 2.2f));
        var maxX = Math.Min(width - 1, (int)Math.Ceiling(cx + radius * 2.2f));
        var minY = Math.Max(0, (int)Math.Floor(cy - radius * 2.2f));
        var maxY = Math.Min(height - 1, (int)Math.Ceiling(cy + radius * 2.2f));
        var inv = 1.0 / Math.Max(0.001f, radius);

        for (var y = minY; y <= maxY; y++)
        {
            var dy = (y - cy) * inv;
            for (var x = minX; x <= maxX; x++)
            {
                var dx = (x - cx) * inv;
                var d = (float)Math.Sqrt(dx * dx + dy * dy);
                if (d > 2.0f)
                {
                    continue;
                }

                var k = MathF.Exp(-d * d * 1.3f) * alpha;
                BlendPixel(buffer, width, x, y, 180, 220, 255, k, true);
            }
        }
    }

    private static void DrawEqualizer(byte[] buffer, int width, int height, AnalysisFrame frame, float[] smoothed)
    {
        var bands = frame.Bands;
        var count = Math.Min(bands.Length, smoothed.Length);
        if (count == 0)
        {
            return;
        }

        var marginX = width * 0.085;
        var baseY = height * 0.91;
        var maxH = height * 0.291;
        var barW = Math.Max(1.8, (width * 0.84) / count * 0.79);
        var gap = Math.Max(0.45, ((width * 0.84) - barW * count) / Math.Max(1, count - 1));

        for (var i = 0; i < count; i++)
        {
            var target = bands[i];
            smoothed[i] = smoothed[i] * 0.78f + target * 0.22f;
            var h = Math.Max(2.8, smoothed[i] * maxH);
            var x0 = marginX + i * (barW + gap);
            var y0 = baseY - h;
            var intensity = Math.Clamp(smoothed[i] * 1.35 + frame.Energy * 0.35, 0.0, 1.0);
            var r = 110 + 145 * intensity;
            var g = 220 + 35 * intensity;
            var b = 170 + 85 * intensity;
            FillRectAdditive(buffer, width, height, x0, y0, barW, h, r, g, b, 0.56 + intensity * 0.36);
        }
    }

    private static void FillRectAdditive(
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
        var left = Math.Clamp((int)Math.Floor(x), 0, width - 1);
        var top = Math.Clamp((int)Math.Floor(y), 0, height - 1);
        var right = Math.Clamp((int)Math.Ceiling(x + w), left + 1, width);
        var bottom = Math.Clamp((int)Math.Ceiling(y + h), top + 1, height);

        for (var yy = top; yy < bottom; yy++)
        {
            for (var xx = left; xx < right; xx++)
            {
                BlendPixel(buffer, width, xx, yy, r, g, b, alpha, true);
            }
        }
    }

    private static void Sample(RgbaImage image, double x, double y, out double r, out double g, out double b, out double a)
    {
        var x0 = Math.Clamp((int)Math.Floor(x), 0, image.Width - 1);
        var y0 = Math.Clamp((int)Math.Floor(y), 0, image.Height - 1);
        var x1 = Math.Min(x0 + 1, image.Width - 1);
        var y1 = Math.Min(y0 + 1, image.Height - 1);
        var tx = x - x0;
        var ty = y - y0;
        var i00 = (y0 * image.Width + x0) * 4;
        var i10 = (y0 * image.Width + x1) * 4;
        var i01 = (y1 * image.Width + x0) * 4;
        var i11 = (y1 * image.Width + x1) * 4;

        static double Lerp(double a0, double a1, double t) => a0 + (a1 - a0) * t;
        var r0 = Lerp(image.Pixels[i00], image.Pixels[i10], tx);
        var g0 = Lerp(image.Pixels[i00 + 1], image.Pixels[i10 + 1], tx);
        var b0 = Lerp(image.Pixels[i00 + 2], image.Pixels[i10 + 2], tx);
        var a0 = Lerp(image.Pixels[i00 + 3], image.Pixels[i10 + 3], tx);
        var r1 = Lerp(image.Pixels[i01], image.Pixels[i11], tx);
        var g1 = Lerp(image.Pixels[i01 + 1], image.Pixels[i11 + 1], tx);
        var b1 = Lerp(image.Pixels[i01 + 2], image.Pixels[i11 + 2], tx);
        var a1 = Lerp(image.Pixels[i01 + 3], image.Pixels[i11 + 3], tx);

        r = Lerp(r0, r1, ty);
        g = Lerp(g0, g1, ty);
        b = Lerp(b0, b1, ty);
        a = Lerp(a0, a1, ty) / 255.0;
    }

    private static void BlendPixel(byte[] dest, int width, int x, int y, double sr, double sg, double sb, double sa, bool additive)
    {
        if (sa <= 0)
        {
            return;
        }

        var idx = (y * width + x) * 4;
        if (additive)
        {
            dest[idx] = (byte)Math.Clamp(dest[idx] + sr * sa, 0, 255);
            dest[idx + 1] = (byte)Math.Clamp(dest[idx + 1] + sg * sa, 0, 255);
            dest[idx + 2] = (byte)Math.Clamp(dest[idx + 2] + sb * sa, 0, 255);
            return;
        }

        var inv = 1.0 - sa;
        dest[idx] = (byte)Math.Clamp(sr * sa + dest[idx] * inv, 0, 255);
        dest[idx + 1] = (byte)Math.Clamp(sg * sa + dest[idx + 1] * inv, 0, 255);
        dest[idx + 2] = (byte)Math.Clamp(sb * sa + dest[idx + 2] * inv, 0, 255);
    }

    private sealed class DustParticle
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        public float Vx { get; set; }
        public float Vy { get; set; }
        public float Radius { get; set; }
        public float Alpha { get; set; }
        public float Flicker { get; set; }
    }
}
