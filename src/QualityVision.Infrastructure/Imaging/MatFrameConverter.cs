using System.Buffers;

using OpenCvSharp;

using QualityVision.Core.Frames;

namespace QualityVision.Infrastructure.Imaging;

/// <summary>Converts an OpenCV image (BGR, gray or BGRA) into a pooled BGRA32 Core <see cref="Frame"/>.</summary>
internal static class MatFrameConverter
{
    private static readonly Action<byte[]> ReturnToPool = static buffer => ArrayPool<byte>.Shared.Return(buffer);

    /// <summary>
    /// Downscales <paramref name="source"/> so its longest side is at most <paramref name="maxSide"/> (0 = no limit),
    /// converts it to BGRA directly into a buffer rented from <see cref="ArrayPool{T}.Shared"/> and wraps it in a
    /// <see cref="Frame"/>. The caller keeps ownership of <paramref name="source"/>; the returned frame owns the
    /// rented buffer and returns it to the pool on dispose.
    /// </summary>
    public static Frame ToBgraFrame(Mat source, int maxSide, long sequence, DateTimeOffset capturedAt, string sourceName)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Empty())
        {
            throw new ArgumentException("Source image is empty.", nameof(source));
        }

        using var resized = new Mat();
        Mat input = source;
        int longest = Math.Max(source.Cols, source.Rows);
        if (maxSide > 0 && longest > maxSide)
        {
            double scale = (double)maxSide / longest;
            var size = new Size(
                Math.Max(1, (int)Math.Round(source.Cols * scale)),
                Math.Max(1, (int)Math.Round(source.Rows * scale)));
            Cv2.Resize(source, resized, size, 0, 0, InterpolationFlags.Area);
            input = resized;
        }

        ColorConversionCodes? conversion = input.Channels() switch
        {
            1 => ColorConversionCodes.GRAY2BGRA,
            3 => ColorConversionCodes.BGR2BGRA,
            4 => null,
            int channels => throw new NotSupportedException($"Images with {channels} channels are not supported."),
        };

        int width = input.Cols;
        int height = input.Rows;
        byte[] buffer = ArrayPool<byte>.Shared.Rent(checked(width * 4 * height));
        try
        {
            using (var target = new PinnedMat(buffer, height, width, MatType.CV_8UC4, 4))
            {
                if (conversion is { } code)
                {
                    Cv2.CvtColor(input, target.Mat, code);
                }
                else
                {
                    input.CopyTo(target.Mat);
                }

                // If OpenCV had to reallocate (unexpected depth/size), the pooled buffer was never written.
                if (target.Mat.Data != target.Address)
                {
                    throw new InvalidOperationException("OpenCV reallocated the destination; the pooled buffer was not filled.");
                }
            }

            // Ownership of the rented buffer transfers to the Frame, which returns it to the pool on Dispose.
            return new Frame(buffer, width, height, sequence, capturedAt, sourceName, ReturnToPool);
        }
        catch
        {
            // The Frame was not created, so nobody else will return the buffer.
            ArrayPool<byte>.Shared.Return(buffer);
            throw;
        }
    }
}
