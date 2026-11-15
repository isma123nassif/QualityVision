using System.ComponentModel.DataAnnotations;

namespace QualityVision.Web.Pipeline;

/// <summary>MJPEG output settings for <c>GET /stream</c> (section "Stream").</summary>
public sealed class StreamOptions
{
    public const string SectionName = "Stream";

    /// <summary>Upper bound for frames encoded and published per second.</summary>
    [Range(1, 60)]
    public int MaxFps { get; set; } = 15;

    [Range(10, 100)]
    public int JpegQuality { get; set; } = 75;

    /// <summary>Frames wider than this are downscaled before JPEG encoding.</summary>
    [Range(160, 3840)]
    public int MaxWidth { get; set; } = 1280;
}
