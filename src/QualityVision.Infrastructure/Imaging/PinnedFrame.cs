using System.Runtime.InteropServices;

using QualityVision.Core.Frames;

using SkiaSharp;

namespace QualityVision.Infrastructure.Imaging;

/// <summary>
/// Pins a <see cref="Frame"/>'s BGRA buffer so Skia can read it in place — no pixel copy.
/// Anything created over <see cref="Address"/> (<see cref="SKBitmap"/>, <see cref="SKPixmap"/>, <see cref="SKImage"/>)
/// MUST be disposed before this object; declare this <c>using</c> first so it is disposed last.
/// The caller keeps ownership of the frame and must keep it alive (undisposed) until this is disposed.
/// </summary>
internal sealed class PinnedFrame : IDisposable
{
    private GCHandle _handle;

    public PinnedFrame(Frame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        // UnsafeBuffer throws ObjectDisposedException if the frame was already disposed.
        byte[] buffer = frame.UnsafeBuffer;
        Info = new SKImageInfo(frame.Width, frame.Height, SKColorType.Bgra8888, SKAlphaType.Opaque);
        RowBytes = frame.Stride;
        _handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        Address = _handle.AddrOfPinnedObject();
    }

    /// <summary>Pixel layout: BGRA8888, opaque, <c>Width x Height</c> of the frame.</summary>
    public SKImageInfo Info { get; }

    /// <summary>Bytes per row (<see cref="Frame.Stride"/>).</summary>
    public int RowBytes { get; }

    /// <summary>Address of the first pixel; valid only until <see cref="Dispose"/>.</summary>
    public nint Address { get; }

    /// <summary>
    /// Creates an <see cref="SKBitmap"/> header over the pinned pixels (no copy, no release callback).
    /// The caller owns the bitmap and must dispose it before this object.
    /// </summary>
    public SKBitmap CreateBitmap()
    {
        var bitmap = new SKBitmap();
        try
        {
            if (!bitmap.InstallPixels(Info, Address, RowBytes))
            {
                throw new InvalidOperationException("SKBitmap.InstallPixels failed for the frame buffer.");
            }

            return bitmap; // Ownership transfers to the caller.
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        GCHandle handle = _handle;
        _handle = default;
        if (handle.IsAllocated)
        {
            handle.Free();
        }
    }
}
