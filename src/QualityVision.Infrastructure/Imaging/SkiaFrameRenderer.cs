using System.Globalization;

using QualityVision.Core.Detection;
using QualityVision.Core.Frames;
using QualityVision.Core.Geometry;
using QualityVision.Core.Inspection;
using QualityVision.Core.Rules;

using SkiaSharp;

namespace QualityVision.Infrastructure.Imaging;

/// <summary>
/// Renders the annotated JPEG for the MJPEG stream: the frame (downscaled to <c>maxWidth</c>), the inspection zone
/// ("Prüfzone") and one box + German label per detection. Thick lines and large, high-contrast labels so the image
/// stays legible after Google Meet compression. Singleton: the bundled font is loaded once; every other Skia object
/// is created and disposed per call. The frame itself is never modified.
/// </summary>
public sealed class SkiaFrameRenderer : IDisposable
{
    private const string ZoneLabel = "Prüfzone";
    private const float ReferenceWidth = 1280f;
    private const byte OutsideZoneAlpha = 153; // ~60 %

    private static readonly SKColor EmptyZoneColor = SKColor.Parse("#e5e7eb");
    private static readonly SKColor OkColor = SKColor.Parse("#22c55e");
    private static readonly SKColor NokColor = SKColor.Parse("#ef4444");
    private static readonly SKColor DangerColor = SKColor.Parse("#f97316");
    private static readonly SKColor UnknownColor = SKColor.Parse("#9ca3af");
    private static readonly SKColor LabelBackground = new(17, 24, 39, 230); // #111827, ~90 % opaque
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");
    private static readonly SKSamplingOptions DownscaleSampling = new(SKFilterMode.Linear, SKMipmapMode.None);

    private readonly SKTypeface _typeface;

    /// <summary>Loads the bundled font from <c>AppContext.BaseDirectory/Assets/Fonts/DejaVuSans-Bold.ttf</c>.</summary>
    public SkiaFrameRenderer()
        : this(Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts", "DejaVuSans-Bold.ttf"))
    {
    }

    /// <summary>Loads the label font from <paramref name="fontPath"/>. There is no fontconfig, so no system fallback.</summary>
    /// <param name="fontPath">Path of the TrueType font file to use for all labels.</param>
    internal SkiaFrameRenderer(string fontPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fontPath);
        if (!File.Exists(fontPath))
        {
            throw new FileNotFoundException("Label font not found; it should be copied to the output folder.", fontPath);
        }

        _typeface = SKTypeface.FromFile(fontPath)
            ?? throw new InvalidOperationException($"Skia could not load the font '{fontPath}'.");
    }

    /// <summary>
    /// Returns a JPEG of <paramref name="frame"/> scaled to at most <paramref name="maxWidth"/> pixels wide
    /// (0 or less = no limit, aspect kept), with <paramref name="zone"/> (if valid) and <paramref name="detections"/> drawn.
    /// Boxes are coloured by verdict; <see cref="Verdict.Ignore"/> is not drawn; objects outside the zone are dimmed.
    /// </summary>
    /// <param name="frame">Source frame; never modified.</param>
    /// <param name="detections">Classified detections to draw (normalised boxes).</param>
    /// <param name="zone">Inspection zone to draw, or null for none.</param>
    /// <param name="quality">JPEG quality, clamped to 1–100.</param>
    /// <param name="maxWidth">Maximum output width in pixels; 0 or less = no limit.</param>
    public byte[] RenderJpeg(Frame frame, IReadOnlyList<ClassifiedDetection> detections, NormalizedRect? zone, int quality,
        int maxWidth)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(detections);

        int width = frame.Width;
        int height = frame.Height;
        if (maxWidth > 0 && width > maxWidth)
        {
            height = Math.Max(1, (int)Math.Round((double)frame.Height * maxWidth / frame.Width));
            width = maxWidth;
        }

        var outputInfo = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using SKSurface surface = SKSurface.Create(outputInfo)
            ?? throw new InvalidOperationException($"Could not create a {width}x{height} Skia surface.");
        SKCanvas canvas = surface.Canvas; // Owned by the surface; not disposed separately.

        DrawFrame(canvas, frame, new SKRect(0, 0, width, height));

        // Sizes are defined at 1280 px wide and scale with the output (minimums keep small outputs legible).
        float scale = width / ReferenceWidth;
        float stroke = MathF.Max(3f, MathF.Round(4f * scale));
        using var font = new SKFont(_typeface, MathF.Max(14f, 28f * scale)) { Edging = SKFontEdging.Antialias };
        using var zoneFont = new SKFont(_typeface, MathF.Max(16f, 32f * scale)) { Edging = SKFontEdging.Antialias };
        using var strokePaint = new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = stroke, IsAntialias = true };
        using var fillPaint = new SKPaint { Style = SKPaintStyle.Fill, IsAntialias = true };

        NormalizedRect? validZone = zone is { IsValid: true } ? zone : null;

        foreach (ClassifiedDetection item in detections)
        {
            SKRect rect = ToPixels(item.Detection.Box, width, height);
            if (item.Verdict == Verdict.Ignore || rect.Width <= 0 || rect.Height <= 0)
            {
                continue;
            }

            bool inZone = validZone is { } z && InspectionZone.IsInside(item.Detection, z);
            bool full = validZone is null || inZone;
            byte alpha = full ? (byte)255 : OutsideZoneAlpha;
            SKColor color = VerdictColor(item.Verdict).WithAlpha(alpha);
            strokePaint.Color = color;
            strokePaint.StrokeWidth = full ? stroke : MathF.Max(2f, MathF.Round(stroke * 0.6f));
            canvas.DrawRect(rect, strokePaint);

            // "P0" with de-DE → "82 %" (non-breaking space), e.g. "Tasse 82 % · n.i.O.".
            string label = string.Create(German,
                $"{GermanLabels.Get(item.Detection.ClassName)} {item.Detection.Confidence:P0} · {VerdictLabels.Get(item.Verdict)}");
            DrawLabel(canvas, label, rect, color, font, fillPaint, stroke, width, height, alpha);
        }

        if (validZone is { } drawnZone)
        {
            // Zone colour = most severe non-ignored verdict inside; drawn last so it stays on top.
            Verdict? worst = null;
            foreach (ClassifiedDetection item in detections)
            {
                if (item.Verdict != Verdict.Ignore && InspectionZone.IsInside(item.Detection, drawnZone)
                    && (worst is null || VerdictSeverity.Rank(item.Verdict) > VerdictSeverity.Rank(worst.Value)))
                {
                    worst = item.Verdict;
                }
            }

            SKColor zoneColor = worst is { } w ? VerdictColor(w) : EmptyZoneColor;
            bool severe = worst is Verdict.Nok or Verdict.Danger;
            float zoneStroke = severe ? MathF.Round(stroke * 1.5f) : stroke;
            string zoneText = severe ? $"{ZoneLabel} · {VerdictLabels.Get(worst!.Value)}" : ZoneLabel;

            SKRect rect = ToPixels(drawnZone, width, height);
            strokePaint.Color = zoneColor;
            strokePaint.StrokeWidth = zoneStroke;
            canvas.DrawRect(rect, strokePaint);
            DrawLabel(canvas, zoneText, rect, zoneColor, zoneFont, fillPaint, zoneStroke, width, height, 255);
        }

        using SKImage snapshot = surface.Snapshot();
        using SKData jpeg = snapshot.Encode(SKEncodedImageFormat.Jpeg, Math.Clamp(quality, 1, 100))
            ?? throw new InvalidOperationException("JPEG encoding failed.");
        return jpeg.ToArray();
    }

    public void Dispose() => _typeface.Dispose();

    /// <summary>Draws the frame's pixels (wrapped in place, no copy) scaled into <paramref name="destination"/>.</summary>
    private static void DrawFrame(SKCanvas canvas, Frame frame, SKRect destination)
    {
        // Disposal runs in reverse declaration order: image → pixmap → pin. The raster canvas draws immediately,
        // so nothing references the frame buffer once this method returns.
        using var pinned = new PinnedFrame(frame);
        using var pixmap = new SKPixmap(pinned.Info, pinned.Address, pinned.RowBytes);
        using SKImage image = SKImage.FromPixels(pixmap)
            ?? throw new InvalidOperationException("Could not wrap the frame pixels in an SKImage.");
        canvas.DrawImage(image, destination, DownscaleSampling);
    }

    /// <summary>Label with a dark rounded background above <paramref name="box"/>, or just inside it if there is no room; <paramref name="alpha"/> dims it.</summary>
    private static void DrawLabel(SKCanvas canvas, string text, SKRect box, SKColor textColor, SKFont font,
        SKPaint fillPaint, float stroke, int width, int height, byte alpha)
    {
        SKFontMetrics metrics = font.Metrics;
        float padding = MathF.Round(font.Size * 0.3f);
        float textWidth = font.MeasureText(text);
        float textHeight = metrics.Descent - metrics.Ascent; // Ascent is negative.
        float labelWidth = textWidth + (2 * padding);
        float labelHeight = textHeight + (2 * padding);

        float half = stroke / 2;
        float left = Math.Clamp(box.Left - half, 0, MathF.Max(0, width - labelWidth));
        float top = box.Top - half - labelHeight;
        if (top < 0)
        {
            top = Math.Clamp(box.Top + half, 0, MathF.Max(0, height - labelHeight));
        }

        var background = new SKRect(left, top, left + labelWidth, top + labelHeight);
        fillPaint.Color = LabelBackground.WithAlpha((byte)(LabelBackground.Alpha * alpha / 255));
        canvas.DrawRoundRect(background, padding, padding, fillPaint);

        fillPaint.Color = textColor;
        canvas.DrawText(text, left + padding, top + padding - metrics.Ascent, font, fillPaint);
    }

    private static SKColor VerdictColor(Verdict verdict) => verdict switch
    {
        Verdict.Ok => OkColor,
        Verdict.Nok => NokColor,
        Verdict.Danger => DangerColor,
        _ => UnknownColor,
    };

    private static SKRect ToPixels(NormalizedRect rect, int width, int height) =>
        new((float)(rect.X * width), (float)(rect.Y * height),
            (float)((rect.X + rect.Width) * width), (float)((rect.Y + rect.Height) * height));
}
