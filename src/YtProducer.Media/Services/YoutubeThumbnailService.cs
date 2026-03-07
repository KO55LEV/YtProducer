using YtProducer.Media.Models;
using SkiaSharp;

namespace YtProducer.Media.Services;

public sealed class YoutubeThumbnailService
{
    public Task<CreateYoutubeThumbnailResponse> CreateAsync(CreateYoutubeThumbnailRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var imagePath = Path.GetFullPath(request.ImagePath!);
        var outputPath = Path.GetFullPath(request.OutputPath!);
        var logoPath = string.IsNullOrWhiteSpace(request.LogoPath) ? null : Path.GetFullPath(request.LogoPath);

        using var source = SKBitmap.Decode(imagePath) ?? throw new InvalidOperationException("Failed to decode input image.");
        using var surface = SKSurface.Create(new SKImageInfo(source.Width, source.Height, SKColorType.Rgba8888, SKAlphaType.Premul))
            ?? throw new InvalidOperationException("Failed to create output surface.");
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Black);
        canvas.DrawBitmap(source, new SKRect(0, 0, source.Width, source.Height));

        var saliency = BuildSaliencyMap(source, step: 4);
        var subjectRect = EstimateSubjectRect(source.Width, source.Height, saliency);
        var layout = SelectBestLayout(
            source.Width,
            source.Height,
            subjectRect,
            saliency);

        DrawTextPanel(canvas, layout.PanelRect);
        DrawText(canvas, request.Headline!, layout.HeadlineRect, ResolveTypeface(request.Style?.HeadlineFont), ParseColor(request.Style?.HeadlineColor, new SKColor(242, 255, 248)), request.Style?.Shadow ?? true, request.Style?.Stroke ?? true);
        DrawText(canvas, request.Subheadline!, layout.SubheadlineRect, ResolveTypeface(request.Style?.SubheadlineFont), ParseColor(request.Style?.SubheadlineColor, new SKColor(214, 234, 226)), request.Style?.Shadow ?? true, request.Style?.Stroke ?? true);

        var logoRect = new SKRectI(0, 0, 0, 0);
        if (!string.IsNullOrWhiteSpace(logoPath) && File.Exists(logoPath))
        {
            logoRect = DrawLogo(canvas, source.Width, source.Height, logoPath);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? Directory.GetCurrentDirectory());
        using var image = surface.Snapshot();
        using var encoded = EncodeByExtension(image, outputPath);
        using var outputStream = File.Open(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
        encoded.SaveTo(outputStream);

        var response = new CreateYoutubeThumbnailResponse
        {
            Ok = true,
            OutputPath = outputPath,
            Layout = new CreateYoutubeThumbnailLayoutResponse
            {
                HeadlineBox = ToBox(layout.HeadlineRect),
                SubheadlineBox = ToBox(layout.SubheadlineRect),
                LogoBox = ToBox(logoRect),
                SafeSubjectMaskScore = Math.Round(layout.SafeSubjectMaskScore, 3)
            }
        };

        return Task.FromResult(response);
    }

    private static (SKRectI HeadlineRect, SKRectI SubheadlineRect, SKRectI PanelRect, double SafeSubjectMaskScore) SelectBestLayout(
        int width,
        int height,
        SKRectI subjectRect,
        SaliencyMap saliency)
    {
        var marginX = (int)Math.Round(width * 0.05);
        var marginY = (int)Math.Round(height * 0.05);
        var areaWidth = (int)Math.Round(width * 0.58);
        var headHeight = (int)Math.Round(height * 0.17);
        var subHeight = (int)Math.Round(height * 0.09);
        var gap = (int)Math.Round(height * 0.018);
        var headZoneBottom = subjectRect.Top + (int)Math.Round(subjectRect.Height * 0.45);
        var headZone = ClampRect(
            new SKRectI(subjectRect.Left, subjectRect.Top, subjectRect.Right, headZoneBottom),
            width,
            height);

        var candidates = new List<(SKRectI H, SKRectI S)>
        {
            (
                new SKRectI(marginX, marginY, marginX + areaWidth, marginY + headHeight),
                new SKRectI(marginX, marginY + headHeight + gap, marginX + areaWidth, marginY + headHeight + gap + subHeight)
            ),
            (
                new SKRectI(width - marginX - areaWidth, marginY, width - marginX, marginY + headHeight),
                new SKRectI(width - marginX - areaWidth, marginY + headHeight + gap, width - marginX, marginY + headHeight + gap + subHeight)
            ),
            (
                new SKRectI(marginX, height - marginY - (headHeight + gap + subHeight), marginX + areaWidth, height - marginY - (subHeight + gap)),
                new SKRectI(marginX, height - marginY - subHeight, marginX + areaWidth, height - marginY)
            )
        };

        (SKRectI H, SKRectI S, double Score) best = (candidates[0].H, candidates[0].S, double.MaxValue);
        foreach (var candidate in candidates)
        {
            var overlapScore = OverlapScore(subjectRect, candidate.H, candidate.S);
            var saliencyScore = SampleScore(saliency, candidate.H, candidate.S);
            var headOverlap = OverlapScore(headZone, candidate.H, candidate.S);
            var total = overlapScore * 0.7 + saliencyScore * 0.3 + headOverlap * 0.45;
            if (total < best.Score)
            {
                best = (candidate.H, candidate.S, total);
            }
        }

        var panelRect = Union(best.H, best.S);
        panelRect.Inflate((int)Math.Round(width * 0.015), (int)Math.Round(height * 0.012));
        panelRect = ClampRect(panelRect, width, height);

        var safeScore = Math.Clamp(1.0 - best.Score, 0.0, 1.0);
        return (best.H, best.S, panelRect, safeScore);
    }

    private static SKRectI EstimateSubjectRect(int width, int height, SaliencyMap saliency)
    {
        var cx = width / 2.0;
        var cy = height / 2.0;

        if (saliency.TotalWeight > 0)
        {
            cx = saliency.WeightedX / saliency.TotalWeight;
            cy = saliency.WeightedY / saliency.TotalWeight;
        }

        var subjectWidth = (int)Math.Round(width * 0.4);
        var subjectHeight = (int)Math.Round(height * 0.62);
        var left = (int)Math.Round(cx - subjectWidth / 2.0);
        var top = (int)Math.Round(cy - subjectHeight / 2.0);
        var rect = new SKRectI(left, top, left + subjectWidth, top + subjectHeight);
        return ClampRect(rect, width, height);
    }

    private static SaliencyMap BuildSaliencyMap(SKBitmap bitmap, int step)
    {
        var width = bitmap.Width;
        var height = bitmap.Height;
        var weights = new float[width, height];
        double totalWeight = 0;
        double weightedX = 0;
        double weightedY = 0;

        for (var y = 1; y < height - 1; y += step)
        {
            for (var x = 1; x < width - 1; x += step)
            {
                var c = bitmap.GetPixel(x, y);
                var l = Luma(c);
                var lx = Luma(bitmap.GetPixel(x + 1, y));
                var ly = Luma(bitmap.GetPixel(x, y + 1));
                var gx = Math.Abs(lx - l);
                var gy = Math.Abs(ly - l);
                var sat = Saturation(c);
                var weight = (float)Math.Clamp((gx + gy) * 0.7 + sat * 0.3, 0, 1);

                weights[x, y] = weight;
                totalWeight += weight;
                weightedX += x * weight;
                weightedY += y * weight;
            }
        }

        return new SaliencyMap(weights, width, height, totalWeight, weightedX, weightedY, step);
    }

    private static void DrawTextPanel(SKCanvas canvas, SKRectI panelRect)
    {
        var panel = new SKRect(panelRect.Left, panelRect.Top, panelRect.Right, panelRect.Bottom);
        using var paint = new SKPaint
        {
            IsAntialias = true,
            Shader = SKShader.CreateLinearGradient(
                new SKPoint(panel.Left, panel.Top),
                new SKPoint(panel.Left, panel.Bottom),
                [new SKColor(0, 0, 0, 180), new SKColor(0, 0, 0, 95)],
                [0f, 1f],
                SKShaderTileMode.Clamp)
        };

        canvas.DrawRoundRect(panel, 20, 20, paint);
    }

    private static void DrawText(
        SKCanvas canvas,
        string text,
        SKRectI rect,
        SKTypeface typeface,
        SKColor color,
        bool withShadow,
        bool withStroke)
    {
        var boundsRect = new SKRect(rect.Left, rect.Top, rect.Right, rect.Bottom);
        var fontSize = FitSingleLineText(text, typeface, boundsRect, 12, 220);
        var centerY = boundsRect.MidY;

        using var fill = new SKPaint
        {
            IsAntialias = true,
            Typeface = typeface,
            TextSize = fontSize,
            Color = color
        };

        if (withShadow)
        {
            fill.ImageFilter = SKImageFilter.CreateDropShadow(0, 4, 6, 6, new SKColor(0, 0, 0, 190));
        }

        if (withStroke)
        {
            using var stroke = new SKPaint
            {
                IsAntialias = true,
                Typeface = typeface,
                TextSize = fontSize,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = Math.Max(2f, fontSize * 0.05f),
                Color = new SKColor(0, 0, 0, 180)
            };
            var strokeBounds = new SKRect();
            stroke.MeasureText(text, ref strokeBounds);
            var sx = boundsRect.Left + (boundsRect.Width - strokeBounds.Width) / 2f - strokeBounds.Left;
            var sy = centerY - (strokeBounds.Top + strokeBounds.Bottom) / 2f;
            canvas.DrawText(text, sx, sy, stroke);
        }

        var bounds = new SKRect();
        fill.MeasureText(text, ref bounds);
        var x = boundsRect.Left + (boundsRect.Width - bounds.Width) / 2f - bounds.Left;
        var y = centerY - (bounds.Top + bounds.Bottom) / 2f;
        canvas.DrawText(text, x, y, fill);
    }

    private static SKRectI DrawLogo(SKCanvas canvas, int width, int height, string logoPath)
    {
        using var logo = SKBitmap.Decode(logoPath);
        if (logo is null || logo.Width <= 0 || logo.Height <= 0)
        {
            return new SKRectI(0, 0, 0, 0);
        }

        // Trim transparent padding so scaling is based on visible logo content.
        var visible = FindVisibleLogoBounds(logo);
        if (visible.Width <= 0 || visible.Height <= 0)
        {
            return new SKRectI(0, 0, 0, 0);
        }

        var margin = (int)Math.Round(width * 0.03);
        var maxWidth = (int)Math.Round(width * 0.15);
        var maxHeight = (int)Math.Round(height * 0.15);
        var scale = Math.Min(maxWidth / (double)visible.Width, maxHeight / (double)visible.Height);
        scale = Math.Min(scale, 1.0);

        var w = (int)Math.Round(visible.Width * scale);
        var h = (int)Math.Round(visible.Height * scale);
        var rect = new SKRectI(width - margin - w, height - margin - h, width - margin, height - margin);

        using var paint = new SKPaint
        {
            IsAntialias = true,
            Color = new SKColor(255, 255, 255, 236),
            FilterQuality = SKFilterQuality.High
        };
        canvas.DrawBitmap(
            logo,
            new SKRect(visible.Left, visible.Top, visible.Right, visible.Bottom),
            new SKRect(rect.Left, rect.Top, rect.Right, rect.Bottom),
            paint);
        return rect;
    }

    private static SKRectI FindVisibleLogoBounds(SKBitmap logo)
    {
        var minX = logo.Width;
        var minY = logo.Height;
        var maxX = -1;
        var maxY = -1;

        for (var y = 0; y < logo.Height; y++)
        {
            for (var x = 0; x < logo.Width; x++)
            {
                if (logo.GetPixel(x, y).Alpha <= 8)
                {
                    continue;
                }

                if (x < minX) minX = x;
                if (y < minY) minY = y;
                if (x > maxX) maxX = x;
                if (y > maxY) maxY = y;
            }
        }

        if (maxX < minX || maxY < minY)
        {
            return new SKRectI(0, 0, 0, 0);
        }

        // Right/Bottom are exclusive for source-rect draw APIs.
        return new SKRectI(minX, minY, maxX + 1, maxY + 1);
    }

    private static float FitSingleLineText(string text, SKTypeface typeface, SKRect area, float min, float max)
    {
        for (var size = max; size >= min; size -= 2f)
        {
            using var paint = new SKPaint
            {
                Typeface = typeface,
                TextSize = size,
                IsAntialias = true
            };

            var bounds = new SKRect();
            paint.MeasureText(text, ref bounds);
            if (bounds.Width <= area.Width * 0.95f && bounds.Height <= area.Height * 0.92f)
            {
                return size;
            }
        }

        return min;
    }

    private static SKTypeface ResolveTypeface(string? fontPathOrFamily)
    {
        if (!string.IsNullOrWhiteSpace(fontPathOrFamily))
        {
            var resolved = Path.GetFullPath(fontPathOrFamily);
            if (File.Exists(resolved))
            {
                var typeface = SKTypeface.FromFile(resolved);
                if (typeface is not null)
                {
                    return typeface;
                }
            }

            var byFamily = SKTypeface.FromFamilyName(fontPathOrFamily, SKFontStyle.Bold);
            if (byFamily is not null)
            {
                return byFamily;
            }
        }

        return SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold) ?? SKTypeface.Default;
    }

    private static SKColor ParseColor(string? hex, SKColor fallback)
    {
        if (!string.IsNullOrWhiteSpace(hex) && SKColor.TryParse(hex, out var parsed))
        {
            return parsed;
        }

        return fallback;
    }

    private static SKData EncodeByExtension(SKImage image, string outputPath)
    {
        var ext = Path.GetExtension(outputPath).ToLowerInvariant();
        return ext switch
        {
            ".png" => image.Encode(SKEncodedImageFormat.Png, 100),
            ".webp" => image.Encode(SKEncodedImageFormat.Webp, 95),
            _ => image.Encode(SKEncodedImageFormat.Jpeg, 94)
        };
    }

    private static double OverlapScore(SKRectI subjectRect, SKRectI headlineRect, SKRectI subheadlineRect)
    {
        var headlineArea = Math.Max(1, headlineRect.Width * headlineRect.Height);
        var subArea = Math.Max(1, subheadlineRect.Width * subheadlineRect.Height);
        var overlapHeadline = IntersectionArea(subjectRect, headlineRect) / (double)headlineArea;
        var overlapSub = IntersectionArea(subjectRect, subheadlineRect) / (double)subArea;
        return Math.Clamp((overlapHeadline + overlapSub) * 0.5, 0, 1);
    }

    private static double SampleScore(SaliencyMap saliency, SKRectI headlineRect, SKRectI subheadlineRect)
    {
        var h = SampleSaliency(saliency, headlineRect);
        var s = SampleSaliency(saliency, subheadlineRect);
        return Math.Clamp((h + s) * 0.5, 0, 1);
    }

    private static double SampleSaliency(SaliencyMap saliency, SKRectI rect)
    {
        var left = Math.Clamp(rect.Left, 0, saliency.Width - 1);
        var right = Math.Clamp(rect.Right, 0, saliency.Width);
        var top = Math.Clamp(rect.Top, 0, saliency.Height - 1);
        var bottom = Math.Clamp(rect.Bottom, 0, saliency.Height);
        if (right <= left || bottom <= top)
        {
            return 1.0;
        }

        double sum = 0;
        var count = 0;
        for (var y = top; y < bottom; y += saliency.Step)
        {
            for (var x = left; x < right; x += saliency.Step)
            {
                sum += saliency.Weights[x, y];
                count++;
            }
        }

        return count == 0 ? 1.0 : Math.Clamp(sum / count, 0, 1);
    }

    private static SKRectI Union(SKRectI a, SKRectI b)
    {
        return new SKRectI(
            Math.Min(a.Left, b.Left),
            Math.Min(a.Top, b.Top),
            Math.Max(a.Right, b.Right),
            Math.Max(a.Bottom, b.Bottom));
    }

    private static SKRectI ClampRect(SKRectI rect, int width, int height)
    {
        var left = Math.Clamp(rect.Left, 0, width);
        var top = Math.Clamp(rect.Top, 0, height);
        var right = Math.Clamp(rect.Right, 0, width);
        var bottom = Math.Clamp(rect.Bottom, 0, height);

        if (right <= left)
        {
            right = Math.Min(width, left + 1);
        }

        if (bottom <= top)
        {
            bottom = Math.Min(height, top + 1);
        }

        return new SKRectI(left, top, right, bottom);
    }

    private static int IntersectionArea(SKRectI a, SKRectI b)
    {
        var left = Math.Max(a.Left, b.Left);
        var right = Math.Min(a.Right, b.Right);
        var top = Math.Max(a.Top, b.Top);
        var bottom = Math.Min(a.Bottom, b.Bottom);
        if (right <= left || bottom <= top)
        {
            return 0;
        }

        return (right - left) * (bottom - top);
    }

    private static float Luma(SKColor c)
    {
        return (0.299f * c.Red + 0.587f * c.Green + 0.114f * c.Blue) / 255f;
    }

    private static float Saturation(SKColor c)
    {
        var r = c.Red / 255f;
        var g = c.Green / 255f;
        var b = c.Blue / 255f;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        return max <= 0 ? 0 : (max - min) / max;
    }

    private static int[] ToBox(SKRectI rect) => [rect.Left, rect.Top, rect.Width, rect.Height];

    private sealed record SaliencyMap(
        float[,] Weights,
        int Width,
        int Height,
        double TotalWeight,
        double WeightedX,
        double WeightedY,
        int Step);
}
