using System.Runtime.InteropServices;

using OpenCvSharp;

namespace QualityVision.Infrastructure.Imaging;

/// <summary>
/// A <see cref="OpenCvSharp.Mat"/> header over a pinned managed byte array — no pixel copy.
/// The array stays pinned until <see cref="Dispose"/>; the Mat must not be used afterwards.
/// The caller keeps ownership of the array itself (e.g. a pooled buffer or a <c>Frame</c> buffer).
/// </summary>
internal sealed class PinnedMat : IDisposable
{
    private GCHandle _handle;

    public PinnedMat(byte[] buffer, int rows, int cols, MatType type, int bytesPerPixel)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rows);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cols);
        long required = (long)rows * cols * bytesPerPixel;
        if (buffer.Length < required)
        {
            throw new ArgumentException($"Buffer holds {buffer.Length} bytes, {required} required.", nameof(buffer));
        }

        _handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try
        {
            Address = _handle.AddrOfPinnedObject();
            Mat = Mat.FromPixelData(rows, cols, type, Address, (long)cols * bytesPerPixel);
        }
        catch
        {
            _handle.Free();
            throw;
        }
    }

    /// <summary>The Mat header; valid only until <see cref="Dispose"/>.</summary>
    public Mat Mat { get; }

    /// <summary>Address of the first pixel, used to detect whether OpenCV reallocated the Mat.</summary>
    public nint Address { get; }

    public void Dispose()
    {
        // Release the header first: it must never outlive the pin.
        Mat.Dispose();
        GCHandle handle = _handle;
        _handle = default;
        if (handle.IsAllocated)
        {
            handle.Free();
        }
    }
}
