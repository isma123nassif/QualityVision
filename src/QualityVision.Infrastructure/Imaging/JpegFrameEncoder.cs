using OpenCvSharp;

using QualityVision.Core.Frames;
using QualityVision.Core.Geometry;

namespace QualityVision.Infrastructure.Imaging;

/// <summary>
/// Encodes a <see cref="Frame"/> to JPEG for the MJPEG stream, drawing the inspection zone ("Prüfzone") with thick
/// lines and a large, high-contrast label so it stays legible after Google Meet compression.
/// Stateless and thread-safe (singleton). M3 extends this to draw detection boxes.
/// </summary>
public sealed class JpegFrameEncoder
{
    // #22c55e as BGR.
    private static readonly Scalar ZoneColor = new(0x5e, 0xc5, 0x22);
    private static readonly Scalar LabelBackground = new(20, 20, 20);
    private const HersheyFonts LabelFont = HersheyFonts.HersheyDuplex;

    /// <summary>
    /// Returns a JPEG of <paramref name="frame"/>, downscaled to at most <paramref name="maxWidth"/> pixels wide
    /// (0 or less = no limit), with the zone drawn if it is valid. The frame itself is never modified.
    /// </summary>
    public byte[] Encode(Frame frame, NormalizedRect? zone, int quality, int maxWidth)
    {
        ArgumentNullException.ThrowIfNull(frame);
        _ = frame.Pixels.Length; // Throws ObjectDisposedException if the frame was already disposed.

        // No copy: the Mat header points at the frame buffer, which the caller keeps alive during this call.
        using var source = new PinnedMat(frame.UnsafeBuffer, frame.Height, frame.Width, MatType.CV_8UC4, 4);
        using var bgr = new Mat();
        Cv2.CvtColor(source.Mat, bgr, ColorConversionCodes.BGRA2BGR);

        // Draw on the (possibly downscaled) copy: the frame stays untouched and line widths match the output size.
        using var resized = new Mat();
        Mat output = bgr;
        if (maxWidth > 0 && frame.Width > maxWidth)
        {
            int height = Math.Max(1, (int)Math.Round((double)frame.Height * maxWidth / frame.Width));
            Cv2.Resize(bgr, resized, new Size(maxWidth, height), 0, 0, InterpolationFlags.Area);
            output = resized;
        }

        if (zone is { IsValid: true } validZone)
        {
            DrawZone(output, validZone);
        }

        int jpegQuality = Math.Clamp(quality, 1, 100);
        Cv2.ImEncode(".jpg", output, out byte[] jpeg, new ImageEncodingParam(ImwriteFlags.JpegQuality, jpegQuality));
        if (jpeg.Length == 0)
        {
            throw new InvalidOperationException("JPEG encoding failed.");
        }

        return jpeg;
    }

    private static void DrawZone(Mat image, NormalizedRect zone)
    {
        int width = image.Cols;
        int height = image.Rows;

        // 3 px at 1280 wide, thicker for larger images.
        int thickness = Math.Max(3, width / 400);
        var rect = new Rect(
            (int)Math.Round(zone.X * width),
            (int)Math.Round(zone.Y * height),
            Math.Max(1, (int)Math.Round(zone.Width * width)),
            Math.Max(1, (int)Math.Round(zone.Height * height)));
        Cv2.Rectangle(image, rect, ZoneColor, thickness, LineTypes.AntiAlias);

        // Label: ~28 px tall text at 1280 wide.
        double fontScale = Math.Max(0.6, width / 1280.0 * 1.1);
        int textThickness = Math.Max(2, thickness - 1);
        int padding = Math.Max(6, width / 200);

        // Hershey fonts are ASCII-only, so the umlaut is transliterated (M2 stopgap; M3 renders text with SkiaSharp).
        const string text = "PRUEFZONE";

        // For Hershey fonts GetTextSize returns: Height = (cap line + descender) * scale + stroke, i.e. everything
        // from the top of the capitals down to the lowest descender, measured upwards from the baseline origin;
        // Width already includes the stroke thickness; baseline = descender depth below the origin (+ half a stroke).
        // So with origin = (x, top + padding + Height) the glyphs span [top + padding, origin.Y + baseline], and a box
        // of Height + baseline + 2 * padding contains them fully, including the stroke.
        Size textSize = Cv2.GetTextSize(text, LabelFont, fontScale, textThickness, out int baseline);
        int labelWidth = textSize.Width + (2 * padding);
        int labelHeight = textSize.Height + baseline + (2 * padding);

        // Above the zone's top-left corner; inside the zone if there is no room above.
        int labelX = Math.Clamp(rect.X - (thickness / 2), 0, Math.Max(0, width - labelWidth));
        int labelY = rect.Y - labelHeight - (thickness / 2);
        if (labelY < 0)
        {
            labelY = Math.Min(rect.Y + thickness, Math.Max(0, height - labelHeight));
        }

        Cv2.Rectangle(image, new Rect(labelX, labelY, labelWidth, labelHeight), LabelBackground, -1);
        var origin = new Point(labelX + padding, labelY + padding + textSize.Height);
        Cv2.PutText(image, text, origin, LabelFont, fontScale, ZoneColor, textThickness, LineTypes.AntiAlias);
    }
}
